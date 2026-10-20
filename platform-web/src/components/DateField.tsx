import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * Date entry for form fields, replacing the browser's own <input type="date">.
 *
 * The native control was the source of the "buggy" dates: it is drawn by each browser in
 * its own way, its typed order follows the device's region (mm/dd/yyyy on many phones and
 * PCs, which reads backwards to a Saudi user), its separate day/month/year segments
 * misbehave inside right-to-left Arabic pages, and it ignores the app's language. This
 * control looks and behaves the same everywhere:
 *
 *  - the chosen date is written out in the active language ("الثلاثاء 20 أكتوبر 2026");
 *  - Today and Yesterday are one tap, which covers almost every daily report;
 *  - a month calendar (weeks starting Saturday) with month and year selectors covers the
 *    rest, including dates years ahead, such as an expected completion date.
 *
 * The value stays a plain "yyyy-mm-dd" calendar date, exactly what the native input
 * produced, so submission and storage are unchanged.
 */

const pad = (n: number) => String(n).padStart(2, '0');

export function toIsoDate(date: Date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Today on this device's calendar - for Saudi users, Saudi local time. */
export function todayIso() {
  return toIsoDate(new Date());
}

function parseIso(value: string): { y: number; m: number; d: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  return match ? { y: Number(match[1]), m: Number(match[2]) - 1, d: Number(match[3]) } : null;
}

/** A calendar date as a UTC instant, so formatting never shifts it by the device's time zone. */
const utc = (y: number, m: number, d: number) => new Date(Date.UTC(y, m, d));

interface DateFieldProps {
  id: string;
  value: string;
  onChange: (value: string) => void;
  required: boolean;
  className: string;
}

export function DateField({ id, value, onChange, required, className }: DateFieldProps) {
  const { t, i18n } = useTranslation();
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  const [open, setOpen] = useState(false);
  const selected = parseIso(value);
  const now = new Date();
  const [view, setView] = useState(() => ({ y: selected?.y ?? now.getFullYear(), m: selected?.m ?? now.getMonth() }));
  const containerRef = useRef<HTMLDivElement>(null);

  // Opening always starts on the selected date's month (or this month).
  useEffect(() => {
    if (open) setView({ y: selected?.y ?? now.getFullYear(), m: selected?.m ?? now.getMonth() });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // Close when the person taps or clicks anywhere outside the field.
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent | TouchEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('touchstart', close);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('touchstart', close);
    };
  }, [open]);

  const longFormat = new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
  const monthFormat = new Intl.DateTimeFormat(locale, { month: 'long', timeZone: 'UTC' });
  const weekdayFormat = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });

  const today = todayIso();
  const yesterdayDate = new Date();
  yesterdayDate.setDate(yesterdayDate.getDate() - 1);
  const yesterday = toIsoDate(yesterdayDate);

  function pick(iso: string) {
    onChange(iso);
    setOpen(false);
  }

  function shiftMonth(delta: number) {
    setView(({ y, m }) => {
      const next = new Date(y, m + delta, 1);
      return { y: next.getFullYear(), m: next.getMonth() };
    });
  }

  // Weeks start on Saturday, the Saudi working week. 3 Jan 2026 was a Saturday.
  const weekdays = Array.from({ length: 7 }, (_, i) => weekdayFormat.format(utc(2026, 0, 3 + i)));
  const firstWeekday = (utc(view.y, view.m, 1).getUTCDay() + 1) % 7; // 0 = Saturday
  const daysInMonth = utc(view.y, view.m + 1, 0).getUTCDate();
  const cells: (number | null)[] = [
    ...Array.from({ length: firstWeekday }, () => null),
    ...Array.from({ length: daysInMonth }, (_, i) => i + 1),
  ];

  const years: number[] = [];
  for (let y = now.getFullYear() - 5; y <= now.getFullYear() + 5; y++) years.push(y);
  if (!years.includes(view.y)) years.push(view.y);
  years.sort((a, b) => a - b);

  const quickClass = (active: boolean) =>
    `rounded border px-3 py-1.5 text-sm ${
      active ? 'border-accent bg-accent text-accent-ink' : 'border-border bg-panel text-ink hover:bg-bg'
    }`;
  const navClass = 'flex h-9 w-9 items-center justify-center rounded border border-border bg-panel text-lg text-ink hover:bg-bg';

  return (
    <div ref={containerRef}>
      <button
        type="button"
        id={id}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className={`${className} flex items-center justify-between gap-2 text-start`}
      >
        <span className={selected ? 'text-ink' : 'text-ink-soft'}>
          {selected ? longFormat.format(utc(selected.y, selected.m, selected.d)) : t('formRenderer.pickDate')}
        </span>
        <span aria-hidden="true" className="text-ink-soft">
          ▾
        </span>
      </button>

      <div className="mt-2 flex flex-wrap gap-2">
        <button type="button" className={quickClass(value === today)} onClick={() => pick(today)}>
          {t('formRenderer.today')}
        </button>
        <button type="button" className={quickClass(value === yesterday)} onClick={() => pick(yesterday)}>
          {t('formRenderer.yesterday')}
        </button>
        {!required && value && (
          <button type="button" className={quickClass(false)} onClick={() => pick('')}>
            {t('formRenderer.clearDate')}
          </button>
        )}
      </div>

      {open && (
        <div role="dialog" aria-label={t('formRenderer.pickDate')} className="mt-2 max-w-sm rounded border border-border bg-panel p-3 shadow-recessed">
          <div className="mb-3 flex items-center gap-2">
            {/* Previous always points left and next always points right, in both languages -
                in Arabic the previous button simply sits on the right, so the two arrows face
                each other. Agreed with Aamer 2026-10-20. Drawn as SVG because text arrows
                (‹ ›) are mirrored by the browser inside right-to-left text. */}
            <button type="button" className={navClass} onClick={() => shiftMonth(-1)} aria-label={t('formRenderer.previousMonth')}>
              <Chevron direction="left" />
            </button>
            <select
              aria-label={t('formRenderer.month')}
              className="min-w-0 flex-1 rounded border border-border bg-panel px-2 py-1.5 text-sm text-ink"
              value={view.m}
              onChange={(e) => setView((v) => ({ ...v, m: Number(e.target.value) }))}
            >
              {Array.from({ length: 12 }, (_, m) => (
                <option key={m} value={m}>
                  {monthFormat.format(utc(2026, m, 1))}
                </option>
              ))}
            </select>
            <select
              aria-label={t('formRenderer.year')}
              className="rounded border border-border bg-panel px-2 py-1.5 text-sm text-ink"
              value={view.y}
              onChange={(e) => setView((v) => ({ ...v, y: Number(e.target.value) }))}
            >
              {years.map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
            <button type="button" className={navClass} onClick={() => shiftMonth(1)} aria-label={t('formRenderer.nextMonth')}>
              <Chevron direction="right" />
            </button>
          </div>

          <div className="grid grid-cols-7 gap-1 text-center">
            {weekdays.map((w) => (
              <div key={w} className="truncate pb-1 text-micro text-ink-soft">
                {w}
              </div>
            ))}
            {cells.map((day, index) => {
              if (day === null) return <div key={`blank-${index}`} />;
              const iso = `${view.y}-${pad(view.m + 1)}-${pad(day)}`;
              const isSelected = iso === value;
              const isToday = iso === today;
              return (
                <button
                  key={iso}
                  type="button"
                  onClick={() => pick(iso)}
                  aria-pressed={isSelected}
                  aria-label={longFormat.format(utc(view.y, view.m, day))}
                  className={`h-10 rounded text-sm ${
                    isSelected
                      ? 'bg-accent font-semibold text-accent-ink'
                      : isToday
                        ? 'border border-accent text-ink hover:bg-bg'
                        : 'text-ink hover:bg-bg'
                  }`}
                >
                  {day}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}

function Chevron({ direction }: { direction: 'left' | 'right' }) {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2.25" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={direction === 'left' ? 'M15 18l-6-6 6-6' : 'M9 6l6 6-6 6'} />
    </svg>
  );
}
