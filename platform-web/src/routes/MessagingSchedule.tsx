import { useCallback, useEffect, useId, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { Button, ErrorState, LoadingState, Sheet, Switch } from '../components/ui';
import { localeFor } from '../forms/recordAnswers';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { DayName, MessagingScheduleDto, ReportRecipientDto, ReportScheduleDto } from '../types';
import { Recipients } from './StakeholderReports';
import { ReminderRecipients } from '../components/ReminderRecipients';

/** The working week, Saturday first (Friday is the day off). */
export const WEEK: DayName[] = ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday'];
const DAY_INDEX: Record<DayName, number> = {
  Sunday: 0,
  Monday: 1,
  Tuesday: 2,
  Wednesday: 3,
  Thursday: 4,
  Friday: 5,
  Saturday: 6,
};
const card = 'flex flex-col gap-4 rounded-ui-2xl border border-border bg-panel px-[18px] py-4';
const timeInput = 'h-[40px] rounded-ui-md border border-border bg-panel px-3 text-[15px] text-ink focus:border-accent focus:outline-none';

/** A weekday's name in the page language (a Sunday-based reference week, so no date maths). */
function dayName(day: DayName, locale: string, width: 'long' | 'short' = 'long') {
  return new Intl.DateTimeFormat(locale, {
    weekday: width,
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(2026, 10, 1 + DAY_INDEX[day])));
}

/** "18:00" in the page language ("6:00 PM" / "6:00 م"). */
export function timeLabel(time: string, locale: string) {
  const [h, m] = time.split(':').map(Number);
  return new Intl.DateTimeFormat(locale, {
    hour: 'numeric',
    minute: '2-digit',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(2026, 0, 1, h || 0, m || 0)));
}

/**
 * Messages (owner, 2026-11-13: "full control of what goes where"): the form reminders (times,
 * days, channels), the new-assignment message, and the stakeholder reports (each one's time,
 * days and who receives it). The server reads this every minute - no restart needed.
 */
export function MessagingSchedule({ token }: { token: string }) {
  const { t, i18n } = useTranslation();
  const locale = localeFor(i18n.language);
  const [saved, setSaved] = useState<MessagingScheduleDto | null>(null);
  const [draft, setDraft] = useState<MessagingScheduleDto | null>(null);
  const [recipients, setRecipients] = useState<ReportRecipientDto[]>([]);
  const [failed, setFailed] = useState(false);
  const [recipientsFailed, setRecipientsFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  // Bumped when either recipients list changes, so the other one reloads too.
  const [peopleVersion, setPeopleVersion] = useState(0);
  // Admin flows F3-3: a warning link pressed with unsaved changes asks first (the app has no
  // general leave guard; BrowserRouter can't block navigation).
  const [leaveTo, setLeaveTo] = useState<string | null>(null);
  const navigate = useNavigate();
  const [error, setError] = useErrorMessage();

  // The recipients come with the reports overview; if that fails, the schedule still loads.
  const loadRecipients = useCallback(async () => {
    try {
      setRecipients((await api.reports.overview(token)).recipients);
      setRecipientsFailed(false);
    } catch {
      setRecipientsFailed(true);
    }
  }, [token]);
  const load = useCallback(async () => {
    setFailed(false);
    try {
      const [schedule] = await Promise.all([api.messagingSchedule.get(token), loadRecipients()]);
      setSaved(schedule);
      setDraft(schedule);
    } catch {
      setFailed(true);
    }
  }, [token, loadRecipients]);
  useEffect(() => {
    void load();
  }, [load]);

  if (failed) return <ErrorState message={t('messages.loadError')} onRetry={() => void load()} />;
  if (!draft || !saved) return <LoadingState />;

  const changed = JSON.stringify(draft) !== JSON.stringify(saved);
  const leave = (to: string) => (changed ? setLeaveTo(to) : navigate(to));
  const set = (patch: Partial<MessagingScheduleDto>) => (setDone(false), setDraft({ ...draft, ...patch }));
  const setReport = (kind: 'daily' | 'weekly' | 'monthly', patch: Partial<ReportScheduleDto>) => set({ [kind]: { ...draft[kind], ...patch } });

  async function save() {
    if (!draft) return;
    setBusy(true);
    setError(null);
    try {
      const next = await api.messagingSchedule.save(token, draft);
      setSaved(next);
      setDraft(next);
      setDone(true);
    } catch (err) {
      setError({ err, fallbackKey: 'messages.saveError' });
    } finally {
      setBusy(false);
    }
  }

  const channelPills = (
    <div className="flex flex-wrap gap-1.5">
      {(
        [
          ['WhatsApp', draft.whatsAppConfigured],
          ['Telegram', draft.telegramConfigured],
        ] as const
      ).map(([name, ok]) => (
        <span key={name} className={`rounded-ui-pill px-3 py-1.5 text-[13px] font-bold ${ok ? 'bg-success-soft text-success' : 'bg-field-muted text-ink-soft'}`}>
          {ok ? t('messages.channelOn', { name }) : t('messages.channelOff', { name })}
        </span>
      ))}
    </div>
  );

  return (
    <div className="mx-auto flex w-full max-w-[860px] flex-col gap-4 pb-24">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-[14px] text-ink-soft">{t('nav.administration')}</span>
          <h1 className="text-[28px] font-extrabold text-ink">{t('messages.title')}</h1>
          <p className="text-[14px] text-ink-soft">{t('messages.intro')}</p>
        </div>
        {channelPills}
      </div>

      {/* ------------------------------------------------------------ reminders */}
      <section className={card}>
        <Toggle title={t('messages.reminders.title')} hint={t('messages.reminders.hint')} checked={draft.remindersEnabled} onChange={(v) => set({ remindersEnabled: v })} />
        <fieldset disabled={!draft.remindersEnabled} className="flex flex-col gap-4 disabled:opacity-50">
          <Row label={t('messages.reminders.times')}>
            <TimeList times={draft.reminderTimes} locale={locale} onChange={(reminderTimes) => set({ reminderTimes })} />
          </Row>
          <Row label={t('messages.days')}>
            <DayChips days={draft.reminderDays} locale={locale} onChange={(reminderDays) => set({ reminderDays })} />
          </Row>
          <Row label={t('messages.sendOn')}>
            <Channels whatsApp={draft.reminderWhatsApp} telegram={draft.reminderTelegram} onChange={(w, tg) => set({ reminderWhatsApp: w, reminderTelegram: tg })} />
          </Row>
        </fieldset>
        <div className="border-t border-divider pt-4">
          <ReminderRecipients token={token} version={peopleVersion} onChanged={() => setPeopleVersion((v) => v + 1)} onNavigate={leave} />
        </div>
        <p className="text-[13px] text-ink-soft">
          {t('messages.reminders.who')}{' '}
          <Link to="/admin/assignments" className="font-bold text-accent-strong underline">
            {t('settings.assignments')}
          </Link>
        </p>
      </section>

      {/* ------------------------------------------------------- new assignment */}
      <section className={card}>
        <Toggle title={t('messages.assigned.title')} hint={t('messages.assigned.hint')} checked={draft.assignedEnabled} onChange={(v) => set({ assignedEnabled: v })} />
        <fieldset disabled={!draft.assignedEnabled} className="disabled:opacity-50">
          <Row label={t('messages.sendOn')}>
            <Channels whatsApp={draft.assignedWhatsApp} telegram={draft.assignedTelegram} onChange={(w, tg) => set({ assignedWhatsApp: w, assignedTelegram: tg })} />
          </Row>
        </fieldset>
        <div className="border-t border-divider pt-4">
          <ReminderRecipients compact token={token} version={peopleVersion} onChanged={() => setPeopleVersion((v) => v + 1)} onNavigate={leave} />
        </div>
      </section>

      {/* -------------------------------------------------------------- reports */}
      <section className={card}>
        <div className="flex flex-col gap-1">
          <h2 className="text-[17px] font-extrabold text-ink">{t('messages.reports.title')}</h2>
          <p className="text-[13px] text-ink-soft">{t('messages.reports.hint')}</p>
        </div>
        {(['daily', 'weekly', 'monthly'] as const).map((kind) => {
          const r = draft[kind];
          return (
            <div key={kind} className="flex flex-col gap-3 rounded-ui-lg border border-divider px-[14px] py-3">
              <Toggle title={t(`reports.kind.${kind}`)} checked={r.enabled} onChange={(v) => setReport(kind, { enabled: v })} />
              <fieldset disabled={!r.enabled} className="flex flex-col gap-3 disabled:opacity-50">
                <Row label={t('messages.time')}>
                  <input type="time" className={timeInput} value={r.time} onChange={(e) => e.target.value && setReport(kind, { time: e.target.value })} aria-label={t('messages.time')} />
                </Row>
                <Row label={kind === 'weekly' ? t('messages.reports.weeklyDay') : t('messages.days')}>
                  {kind === 'daily' && <DayChips days={r.days} locale={locale} onChange={(days) => setReport(kind, { days })} />}
                  {kind === 'weekly' && <DayChips single days={r.days} locale={locale} onChange={(days) => setReport(kind, { days })} />}
                  {kind === 'monthly' && <span className="text-[14px] text-ink">{t('messages.reports.lastDay')}</span>}
                </Row>
              </fieldset>
            </div>
          );
        })}
        <p className="text-[13px] text-ink-soft">{t('messages.reports.channel')}</p>
      </section>

      {recipientsFailed && <ErrorState message={t('messages.recipientsError')} onRetry={() => void loadRecipients()} />}
      <div className={recipientsFailed ? 'hidden' : 'flex'}>
        <Recipients
          token={token}
          recipients={recipients}
          onChanged={async () => {
            await loadRecipients();
          }}
        />
      </div>

      {/* Save bar: shown while there are changes (and "Saved" after), kept at the bottom of the screen. */}
      {(changed || done || error) && (
        <div className="sticky bottom-[calc(var(--tabbar-h)+env(safe-area-inset-bottom)+8px)] z-10 flex flex-wrap items-center gap-3 rounded-ui-2xl border border-border bg-panel px-[18px] py-3 shadow-elev-1 lg:bottom-4">
          <Button size="desktop" disabled={busy || !changed} onClick={() => void save()}>
            {busy ? t('messages.saving') : t('messages.save')}
          </Button>
          {changed && (
            <Button size="desktop" variant="link" disabled={busy} onClick={() => (setDraft(saved), setError(null))}>
              {t('messages.undo')}
            </Button>
          )}
          {error ? (
            <span className="text-[14px] text-danger">{error}</span>
          ) : done && !changed ? (
            <span className="text-[14px] font-bold text-success">{t('messages.saved')}</span>
          ) : changed ? (
            <span className="text-[14px] text-ink-soft">{t('messages.unsaved')}</span>
          ) : null}
        </div>
      )}

      <Sheet open={leaveTo !== null} onClose={() => setLeaveTo(null)} title={t('flows.leaveTitle')} desktopWidth={460}>
        <div className="flex flex-col gap-3 pb-2">
          <p className="text-[14px] text-ink">{t('flows.leaveBody')}</p>
          <div className="flex flex-wrap gap-2">
            <Button size="desktop" variant="danger" onClick={() => leaveTo && navigate(leaveTo)}>
              {t('flows.leaveAnyway')}
            </Button>
            <Button size="desktop" variant="secondary" onClick={() => setLeaveTo(null)}>
              {t('flows.stay')}
            </Button>
          </div>
        </div>
      </Sheet>
    </div>
  );
}

function Toggle({ title, hint, checked, onChange }: { title: string; hint?: string; checked: boolean; onChange: (v: boolean) => void }) {
  const id = useId();
  return (
    <div className="flex items-start gap-3">
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <span id={id} className="text-[16px] font-extrabold text-ink">
          {title}
        </span>
        {hint && <span className="text-[13px] text-ink-soft">{hint}</span>}
      </div>
      <Switch checked={checked} onChange={onChange} labelledBy={id} />
    </div>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:gap-4">
      <span className="text-[14px] font-bold text-ink sm:w-[130px] sm:shrink-0 sm:pt-2">{label}</span>
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  );
}

function TimeList({ times, locale, onChange }: { times: string[]; locale: string; onChange: (times: string[]) => void }) {
  const { t } = useTranslation();
  const [adding, setAdding] = useState('');
  const add = () => {
    if (!adding || times.includes(adding)) return;
    onChange([...times, adding].sort());
    setAdding('');
  };
  return (
    <div className="flex flex-wrap items-center gap-2">
      {times.map((time) => (
        <span key={time} className="inline-flex h-[40px] items-center gap-1 rounded-ui-pill bg-accent-soft ps-3 pe-1 text-[14px] font-bold text-accent-strong">
          <bdi>{timeLabel(time, locale)}</bdi>
          <button
            type="button"
            aria-label={t('messages.removeTime', {
              time: timeLabel(time, locale),
            })}
            onClick={() => onChange(times.filter((x) => x !== time))}
            className="flex h-8 w-8 items-center justify-center rounded-full hover:bg-panel"
          >
            ×
          </button>
        </span>
      ))}
      {times.length < 6 && (
        <span className="inline-flex items-center gap-2">
          <input type="time" className={timeInput} value={adding} onChange={(e) => setAdding(e.target.value)} aria-label={t('messages.newTime')} />
          <Button size="compact" variant="secondary" className="!h-[40px]" disabled={!adding || times.includes(adding)} onClick={add}>
            {t('messages.addTime')}
          </Button>
        </span>
      )}
      {times.length === 0 && <span className="text-[13px] text-danger">{t('messages.needTime')}</span>}
    </div>
  );
}

function DayChips({ days, locale, onChange, single = false }: { days: DayName[]; locale: string; onChange: (days: DayName[]) => void; single?: boolean }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex flex-wrap gap-1.5" role={single ? 'radiogroup' : 'group'}>
        {WEEK.map((day) => {
          const on = days.includes(day);
          return (
            <button
              key={day}
              type="button"
              role={single ? 'radio' : 'checkbox'}
              aria-checked={on}
              onClick={() => onChange(single ? [day] : on ? days.filter((d) => d !== day) : WEEK.filter((d) => d === day || days.includes(d)))}
              className={`h-[40px] min-w-[52px] rounded-ui-pill border px-3 text-[14px] font-bold ${on ? 'border-accent bg-accent text-white' : 'border-border bg-panel text-ink hover:border-ink-soft'}`}
            >
              {dayName(day, locale, 'short')}
            </button>
          );
        })}
      </div>
      {!single && days.length === 0 && <span className="text-[13px] text-danger">{t('messages.needDay')}</span>}
    </div>
  );
}

function Channels({ whatsApp, telegram, onChange }: { whatsApp: boolean; telegram: boolean; onChange: (whatsApp: boolean, telegram: boolean) => void }) {
  return (
    <div className="flex flex-wrap gap-4 text-[14px] text-ink">
      <label className="flex min-h-[40px] items-center gap-2">
        <input type="checkbox" className="h-[18px] w-[18px] accent-[var(--accent)]" checked={whatsApp} onChange={(e) => onChange(e.target.checked, telegram)} />
        WhatsApp
      </label>
      <label className="flex min-h-[40px] items-center gap-2">
        <input type="checkbox" className="h-[18px] w-[18px] accent-[var(--accent)]" checked={telegram} onChange={(e) => onChange(whatsApp, e.target.checked)} />
        Telegram
      </label>
    </div>
  );
}
