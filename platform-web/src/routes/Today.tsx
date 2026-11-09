import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { Icon } from '../components/icons';
import { initial, useFormsByModule, useShell } from '../components/shell/ShellData';
import { Card, DayCell, DayLegend, ErrorState, LoadingState, Pill } from '../components/ui';
import { apiErrorMessage } from '../api/errorMessage';
import type { DayState } from '../components/ui';
import type { AssignmentDayDto } from '../types';
import { useLocalized } from '../forms/localized';

/**
 * Today - the home page for everyone (screens 02 and 10; replaces "Pick a form" and the old My
 * assignments page). Greeting, what is due today (a card per form still to fill, a compact row
 * per form already filled), the last two weeks as day cells, and the other forms the person can
 * open. Data: the my-assignments endpoint (today + the 14 days before) and the forms list.
 */
export function Today() {
  const { t, i18n } = useTranslation();
  const { me, mine, loadError, refresh } = useShell();
  const groups = useFormsByModule();
  const { formName } = useLocalized();
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;

  // Fresh numbers every time the page opens (a form may have just been sent).
  useEffect(() => {
    refresh();
  }, [refresh]);

  const formById = useMemo(() => new Map(groups.flatMap((g) => g.forms).map((f) => [f.id, f])), [groups]);
  const nameOf = (d: AssignmentDayDto) => {
    const f = formById.get(d.formId);
    return f ? formName(f) : d.formName;
  };
  const formModule = useMemo(() => {
    const map = new Map<string, string>();
    for (const g of groups) for (const f of g.forms) map.set(f.id, g.label);
    return map;
  }, [groups]);

  if (!mine || !me) {
    // Kept data stays on screen through a failed refresh; with nothing loaded, say so and offer a retry.
    if (loadError) return <ErrorState className="mx-auto max-w-[720px]" message={apiErrorMessage(loadError, t, 'assignments.mine.loadError')} onRetry={refresh} />;
    return <LoadingState />;
  }

  const todays = mine.days.filter((d) => d.date === mine.today);
  const due = todays.filter((d) => d.status === 'pending');
  const filled = todays.filter((d) => d.status === 'filled');
  const assignedToday = new Set(todays.map((d) => d.formId));
  const otherForms = groups.flatMap((g) => g.forms).filter((f) => !assignedToday.has(f.id));

  // Greeting by the phone's own clock (HANDOFF 02: morning before 12:00, afternoon 12-17, evening after).
  const hour = new Date().getHours();
  const greetingKey = hour < 12 ? 'today.greetingMorning' : hour < 17 ? 'today.greetingAfternoon' : 'today.greetingEvening';
  const dateLine = new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).format(new Date(`${mine.today}T00:00:00Z`));
  const time = (utc: string) => new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', timeZone: 'Asia/Riyadh' }).format(new Date(utc));

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-5">
      <header className="flex items-center justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="text-[14px] text-ink-soft">{dateLine}</span>
          <h1 className="text-[24px] font-extrabold text-ink">{t(greetingKey, { name: me.displayName })}</h1>
        </div>
        <span aria-hidden="true" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-accent-soft text-[17px] font-bold text-accent">
          {initial(me.displayName)}
        </span>
      </header>

      {todays.length > 0 ? (
        <section className="flex flex-col gap-[10px]">
          <div className="flex items-baseline justify-between gap-3">
            <h2 className="text-[17px] font-bold text-ink">{t('today.dueToday')}</h2>
            <span className="text-[14px] text-ink-soft">{t('today.leftOf', { left: due.length, total: todays.length })}</span>
          </div>
          {due.map((d) => (
            <Card key={d.assignmentId} className="flex flex-col gap-[14px] p-[18px]">
              <div className="flex items-start gap-3">
                <span aria-hidden="true" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-ui-lg bg-accent-soft text-accent">
                  <Icon name="clipboard" size={22} />
                </span>
                <div className="flex min-w-0 flex-1 flex-col gap-1">
                  <span className="text-[18px] font-bold text-ink"><bdi>{nameOf(d)}</bdi></span>
                  <span className="text-[14px] text-ink-soft">{t('today.stepsCount', { module: formModule.get(d.formId) ?? '', count: 1 })}</span>
                </div>
                <Pill tone="warning" large>{t('today.dueToday')}</Pill>
              </div>
              <Link
                to={`/forms/${d.formId}`}
                className="flex h-[52px] items-center justify-center gap-2 rounded-ui-xl bg-accent text-[17px] font-bold text-accent-ink hover:brightness-110"
              >
                {t('assignments.mine.fill')}
                <Icon name="chevronForward" size={18} />
              </Link>
            </Card>
          ))}
          {filled.map((d) => (
            <Card key={d.assignmentId} className="flex items-center gap-3 px-[18px] py-[14px]">
              <span aria-hidden="true" className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-success-soft text-success">
                <Icon name="check" size={18} />
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="text-[16px] font-bold text-ink"><bdi>{nameOf(d)}</bdi></span>
                {d.filledAtUtc && <span className="text-[14px] text-ink-soft">{t('today.filledAt', { time: time(d.filledAtUtc) })}</span>}
              </div>
              <Link to="/entries" className="flex min-h-[44px] items-center px-1 text-[15px] font-medium text-accent">
                {t('today.view')}
              </Link>
            </Card>
          ))}
        </section>
      ) : (
        <Card className="p-[18px] text-[16px] text-ink-soft">{t('assignments.mine.noneToday')}</Card>
      )}

      {mine.days.length > 0 && <LastTwoWeeks days={mine.days} today={mine.today} locale={locale} />}

      {/* When everything due today is filled: the note sits right above Other forms (HANDOFF 02). */}
      {todays.length > 0 && due.length === 0 && (
        <p className="flex items-center gap-2 rounded-ui-lg bg-success-soft px-4 py-3 text-[15px] text-success-strong">
          <Icon name="home" size={20} />
          {t('today.allDone')}
        </p>
      )}

      {otherForms.length > 0 && (
        <section className="flex flex-col gap-[10px]">
          <h2 className="text-[17px] font-bold text-ink">{t('today.otherForms')}</h2>
          <Card className="divide-y divide-divider overflow-hidden">
            {otherForms.map((f) => (
              <Link key={f.id} to={`/forms/${f.id}`} className="flex min-h-[56px] items-center gap-3 px-[18px] text-[16px] font-medium text-ink hover:bg-field-muted">
                <span className="flex-1"><bdi>{formName(f)}</bdi></span>
                <Icon name="chevronForward" size={18} className="text-ink-soft" />
              </Link>
            ))}
          </Card>
        </section>
      )}
    </div>
  );
}

/**
 * The last 12 working days (Saturday-Thursday) ending today. A day counts as filled when every
 * form due that day was filled, missed when one wasn't, due when it is today and still open;
 * a working day with nothing assigned is "off".
 */
function LastTwoWeeks({ days, today, locale }: { days: AssignmentDayDto[]; today: string; locale: string }) {
  const { t } = useTranslation();
  const byDate = new Map<string, AssignmentDayDto[]>();
  for (const d of days) byDate.set(d.date, [...(byDate.get(d.date) ?? []), d]);

  const cells: { date: string; state: DayState }[] = [];
  const cursor = new Date(`${today}T00:00:00Z`);
  while (cells.length < 12) {
    if (cursor.getUTCDay() !== 5) {
      const iso = cursor.toISOString().slice(0, 10);
      const list = byDate.get(iso) ?? [];
      const state: DayState =
        list.length === 0 ? 'off' : list.some((d) => d.status === 'missed') ? 'missed' : list.some((d) => d.status === 'pending') ? 'due' : 'filled';
      cells.unshift({ date: iso, state });
    }
    cursor.setUTCDate(cursor.getUTCDate() - 1);
  }

  const past = cells.filter((c) => c.state === 'filled' || c.state === 'missed');
  const narrow = new Intl.DateTimeFormat(locale, { weekday: 'narrow', timeZone: 'UTC' });
  const long = new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' });
  const stateLabel: Record<DayState, string> = {
    filled: t('assignments.status.filled'),
    missed: t('assignments.status.missed'),
    due: t('assignments.status.pending'),
    off: t('assignmentsV2.legendOff'),
  };

  return (
    <Card className="flex flex-col gap-3 px-[18px] py-4">
      <div className="flex items-baseline justify-between gap-3">
        <h2 className="text-[16px] font-bold text-ink">{t('today.lastTwoWeeks')}</h2>
        <span className="text-[14px] text-ink-soft">{t('today.filledOfDays', { filled: past.filter((c) => c.state === 'filled').length, due: past.length })}</span>
      </div>
      <div className="grid grid-cols-12 gap-[5px]">
        {cells.map((c) => {
          const date = new Date(`${c.date}T00:00:00Z`);
          return (
            <div key={c.date} className="flex flex-col items-center gap-1">
              <DayCell state={c.state} size="fill" label={`${long.format(date)}: ${stateLabel[c.state]}`} />
              <span aria-hidden="true" className="text-[11px] text-ink-soft">{narrow.format(date)}</span>
            </div>
          );
        })}
      </div>
      <DayLegend />
    </Card>
  );
}
