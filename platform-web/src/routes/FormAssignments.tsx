import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { builderInput } from '../components/builder/FieldSettings';
import { DateButton } from '../components/DateField';
import { useShell } from '../components/shell/ShellData';
import { Button, DayCell, DayLegend, EmptyState, ErrorState, LoadingState, Sheet, useIsDesktop } from '../components/ui';
import { apiErrorMessage } from '../api/errorMessage';
import type { DayState } from '../components/ui';
import { saudiTodayIso } from '../forms/fieldValues';
import { useLocalized } from '../forms/localized';
import { dayLabel, localeFor } from '../forms/recordAnswers';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { AssignmentDayDto, FormAssignmentDto, FormSummaryDto, UserSummaryDto } from '../types';

const addDays = (iso: string, n: number) => new Date(Date.parse(`${iso}T00:00:00Z`) + n * 86_400_000).toISOString().slice(0, 10);
const isFriday = (iso: string) => new Date(`${iso}T00:00:00Z`).getUTCDay() === 5;

/**
 * Form assignments + daily tracking (screen 23), one page (the old "by day" page redirects here):
 * - Today card: "n of m filled", Filled / Due today / Not filled pills, and "Remind those who
 *   haven't filled (n)" - the existing sender, manual reminders once per person per day.
 * - "Who filled and when": one row per assignment, one cell per day (Fridays greyed), Rate = filled
 *   / due; a filled cell opens that entry; a day header lists who was due that day. Form and range
 *   filters, "Show ended".
 * - Assign panel: form, users (with their Telegram status), from / to, note, "Assign n users".
 */
export function FormAssignments({ token }: { token: string }) {
  const { t, i18n } = useTranslation();
  const { formName } = useLocalized();
  const { forms } = useShell();
  const navigate = useNavigate();
  const desktop = useIsDesktop();
  const locale = localeFor(i18n.language);
  const today = saudiTodayIso();

  const [assignments, setAssignments] = useState<FormAssignmentDto[] | null>(null);
  const [users, setUsers] = useState<UserSummaryDto[]>([]);
  const [days, setDays] = useState<AssignmentDayDto[]>([]);
  const [todayDays, setTodayDays] = useState<AssignmentDayDto[]>([]);
  // The day cells couldn't load: say so, rather than drawing every day as blank.
  const [daysError, setDaysError] = useState<unknown>(null);
  const [rangeDays, setRangeDays] = useState(14);
  const [formFilter, setFormFilter] = useState('');
  const [showEnded, setShowEnded] = useState(false);
  const [tick, setTick] = useState(0);
  const [error, setError] = useErrorMessage();
  const [reminding, setReminding] = useState(false);
  const [remindResult, setRemindResult] = useState<string | null>(null);
  const [dayOpen, setDayOpen] = useState<string | null>(null);
  const panel = useRef<HTMLElement>(null);

  const from = addDays(today, -(rangeDays - 1));
  useEffect(() => {
    Promise.all([api.assignments.list(token), api.users.list(token)])
      .then(([a, u]) => {
        setAssignments(a);
        setUsers(u.filter((x) => x.isActive));
      })
      .catch((err) => setError({ err, fallbackKey: 'assignments.admin.loadError' }));
    setDaysError(null);
    api.assignments.days(token, { from, to: today }).then(setDays).catch((err) => (setDays([]), setDaysError(err)));
    api.assignments.days(token, { from: today, to: today }).then(setTodayDays).catch((err) => (setTodayDays([]), setDaysError(err)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, from, today, tick]);

  const dates = useMemo(() => Array.from({ length: rangeDays }, (_, i) => addDays(from, i)), [from, rangeDays]);
  const byCell = useMemo(() => new Map(days.map((d) => [`${d.assignmentId}|${d.date}`, d])), [days]);
  const rows = (assignments ?? [])
    .filter((a) => (showEnded || a.isActive) && (!formFilter || a.formId === formFilter))
    .filter((a) => a.startDate <= today && (!a.endDate || a.endDate >= from))
    .sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.userName.localeCompare(b.userName));
  const filled = todayDays.filter((d) => d.status === 'filled').length;
  const pending = todayDays.filter((d) => d.status === 'pending');
  const lastReminded = pending.map((d) => d.lastRemindedAtUtc).filter(Boolean).sort().pop();
  const time = (utc: string) => new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', timeZone: 'Asia/Riyadh' }).format(new Date(utc));
  const statusLabel: Record<DayState, string> = {
    filled: t('assignments.status.filled'),
    missed: t('assignments.status.missed'),
    due: t('assignments.status.pending'),
    off: t('assignmentsV2.legendOff'),
  };
  const nameOf = (formId: string, fallback: string) => {
    const f = forms?.find((x) => x.id === formId);
    return f ? formName(f) : fallback;
  };

  async function stop(id: string) {
    try {
      await api.assignments.stop(token, id);
      setTick((x) => x + 1);
    } catch (err) {
      setError({ err, fallbackKey: 'assignments.admin.stopError' });
    }
  }

  async function remind() {
    setReminding(true);
    setRemindResult(null);
    try {
      const r = await api.assignments.remindPending(token);
      setRemindResult(t('assignmentsV2.remindResult', { sent: r.sent, failed: r.failed, already: r.alreadyReminded, none: r.noChannel }));
      setTick((x) => x + 1);
    } catch (err) {
      setError({ err, fallbackKey: 'assignments.admin.loadError' });
    } finally {
      setReminding(false);
    }
  }

  if (!assignments) return error ? <ErrorState message={error} onRetry={() => (setError(null), setTick((x) => x + 1))} /> : <LoadingState />;

  // On a phone the cells are 44px tap targets and the grid scrolls sideways inside its card.
  const cellW = desktop ? 30 : 48;
  const grid = `210px repeat(${dates.length}, ${desktop ? 'minmax(0, 1fr)' : '44px'}) 92px`;
  return (
    <div className="flex flex-col gap-[18px]">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-[14px] text-ink-soft">{t('nav.administration')}</span>
          <h1 className="text-[28px] font-extrabold text-ink">{t('assignments.admin.title')}</h1>
          <p className="text-[14px] text-ink-soft">{t('assignmentsV2.rule')}</p>
        </div>
        <Button size="desktop" onClick={() => panel.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })}>
          {t('assignmentsV2.assignForm')}
        </Button>
      </div>
      {error && <p className="text-[14px] text-danger">{error}</p>}

      <div className="flex flex-wrap items-start gap-4">
        <div className="flex min-w-0 flex-[999_1_560px] flex-col gap-4">
          {/* Today */}
          <section className="flex flex-wrap items-center gap-[18px] rounded-ui-2xl border border-border bg-panel px-[18px] py-4">
            <div className="flex flex-col gap-0.5">
              <span className="text-[13px] text-ink-soft">{t('assignmentsV2.todayLabel', { date: dayLabel(today, locale) })}</span>
              <span className="text-[18px] font-extrabold text-ink">{t('assignmentsV2.filledOfTotal', { filled, total: todayDays.length })}</span>
            </div>
            <div className="flex flex-wrap gap-2">
              <span className="rounded-ui-pill bg-success-soft px-[11px] py-[5px] text-[13px] font-bold text-success">{t('assignmentsV2.pillFilled', { count: filled })}</span>
              <span className="rounded-ui-pill bg-warning-soft px-[11px] py-[5px] text-[13px] font-bold text-warning">{t('assignmentsV2.pillDue', { count: pending.length })}</span>
              <span className="rounded-ui-pill bg-divider px-[11px] py-[5px] text-[13px] font-bold text-ink-soft">{t('assignmentsV2.pillMissed', { count: todayDays.filter((d) => d.status === 'missed').length })}</span>
            </div>
            <span className="flex-1" />
            <div className="flex flex-col items-end gap-1">
              <Button size="compact" variant="secondary" className="!h-[40px] !min-h-[40px] !border !font-normal" disabled={reminding || pending.length === 0} onClick={remind}>
                {t('assignmentsV2.remindPending', { count: pending.length })}
              </Button>
              {lastReminded && <span className="text-[12px] text-ink-soft">{t('assignmentsV2.remindedAt', { time: time(lastReminded) })}</span>}
            </div>
            {remindResult && (
              <p className="basis-full text-[13px] text-ink-soft" role="status">
                {remindResult}
              </p>
            )}
          </section>

          {/* Who filled and when */}
          <section className="flex flex-col gap-[14px] rounded-ui-2xl border border-border bg-panel px-[18px] py-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-[17px] font-extrabold text-ink">{t('assignmentsV2.gridTitle')}</h2>
              <div className="flex flex-wrap items-center gap-1.5">
                <select aria-label={t('assignments.admin.allForms')} value={formFilter} onChange={(e) => setFormFilter(e.target.value)} className="h-[36px] rounded-ui-pill border border-border bg-panel px-3 text-[13px] text-ink">
                  <option value="">{t('assignments.admin.allForms')}</option>
                  {(forms ?? []).filter((f) => f.status === 'Published').map((f: FormSummaryDto) => (
                    <option key={f.id} value={f.id}>
                      {formName(f)}
                    </option>
                  ))}
                </select>
                <select aria-label={t('assignments.admin.period')} value={rangeDays} onChange={(e) => setRangeDays(Number(e.target.value))} className="h-[36px] rounded-ui-pill border border-border bg-panel px-3 text-[13px] text-ink">
                  <option value={14}>{t('assignmentsV2.last14')}</option>
                  <option value={30}>{t('assignmentsV2.last30')}</option>
                  <option value={60}>{t('assignmentsV2.last60')}</option>
                </select>
                <label className="flex h-[36px] items-center gap-1.5 rounded-ui-pill border border-border px-3 text-[13px] text-ink">
                  <input type="checkbox" checked={showEnded} onChange={(e) => setShowEnded(e.target.checked)} />
                  {t('assignmentsV2.showEnded')}
                </label>
              </div>
            </div>
            {daysError ? (
              <ErrorState message={apiErrorMessage(daysError, t, 'assignments.admin.loadError')} onRetry={() => setTick((x) => x + 1)} />
            ) : (
            <div className="overflow-x-auto">
              <div className="flex min-w-[760px] flex-col gap-1.5" style={{ minWidth: Math.max(760, 210 + 92 + dates.length * cellW) }}>
                <div className="grid items-end gap-1 text-center text-[11px] text-ink-soft" style={{ gridTemplateColumns: grid }}>
                  <span />
                  {dates.map((d) => (
                    <button key={d} type="button" onClick={() => setDayOpen(d)} className="flex min-h-[44px] flex-col justify-end leading-[1.3] hover:text-ink lg:min-h-0" aria-label={t('assignmentsV2.dayList', { date: dayLabel(d, locale) })}>
                      <span className="font-bold">{new Intl.DateTimeFormat(locale, { weekday: 'narrow', timeZone: 'UTC' }).format(new Date(`${d}T00:00:00Z`))}</span>
                      <span>{Number(d.slice(8))}</span>
                    </button>
                  ))}
                  <span>{t('assignmentsV2.rate')}</span>
                </div>
                {rows.map((a) => {
                  let due = 0;
                  let done = 0;
                  return (
                    <div key={a.id} className={`group grid items-center gap-1 ${a.isActive ? '' : 'opacity-50'}`} style={{ gridTemplateColumns: grid }}>
                      <span className="flex min-w-0 flex-col">
                        <span className="truncate text-[14px] font-bold text-ink">
                          <bdi>{a.userName}</bdi>
                        </span>
                        <span className="flex min-w-0 items-center gap-1.5 text-[12px] text-ink-soft">
                          <bdi className="truncate">{nameOf(a.formId, a.formName)}</bdi>
                          {!a.isActive && <span>· {t('assignmentsV2.rowEnded')}</span>}
                          {a.isActive && (
                            // The existing "Stop" (ends the assignment today): always shown on a phone (no hover), on hover or keyboard focus on desktop.
                            <button type="button" onClick={() => void stop(a.id)} className="shrink-0 text-danger hover:underline focus:opacity-100 group-hover:opacity-100 lg:opacity-0">
                              {t('assignments.admin.stop')}
                            </button>
                          )}
                        </span>
                      </span>
                      {dates.map((d) => {
                        const day = byCell.get(`${a.id}|${d}`);
                        const state: DayState = !day || isFriday(d) ? 'off' : day.status === 'filled' ? 'filled' : day.status === 'missed' ? 'missed' : 'due';
                        if (state === 'filled' || state === 'missed') due++;
                        if (state === 'filled') done++;
                        const label = t('assignmentsV2.cellLabel', { user: a.userName, date: dayLabel(d, locale), status: statusLabel[state] }) + (day?.lastRemindedAtUtc ? ` · ${t('assignmentsV2.remindedAt', { time: time(day.lastRemindedAtUtc) })}` : '');
                        return (
                          <DayCell
                            key={d}
                            state={state}
                            size="fill"
                            height={desktop ? 30 : 44}
                            label={label}
                            onClick={day?.recordId ? () => navigate(`/entries/${a.formId}/${day.recordId}`) : undefined}
                          />
                        );
                      })}
                      <span className="text-center text-[13px] font-bold text-ink" dir="ltr">
                        {done} / {due}
                      </span>
                    </div>
                  );
                })}
                {rows.length === 0 && <EmptyState title={t('assignments.admin.noAssignments')} body={t('assignmentsV2.emptyBody')} />}
              </div>
            </div>
            )}
            <DayLegend showOff />
          </section>
        </div>

        <AssignPanel
          panelRef={panel}
          token={token}
          users={users}
          onAssigned={() => setTick((x) => x + 1)}
        />
      </div>

      <Sheet open={dayOpen !== null} onClose={() => setDayOpen(null)} title={dayOpen ? t('assignmentsV2.dayList', { date: dayLabel(dayOpen, locale) }) : ''} desktopWidth={520}>
        <ul className="flex flex-col divide-y divide-divider pb-2">
          {days
            .filter((d) => d.date === dayOpen)
            .map((d) => (
              <li key={d.assignmentId} className="flex items-center justify-between gap-3 py-3 text-[14px]">
                <span className="flex min-w-0 flex-col">
                  <span className="font-bold text-ink">
                    <bdi>{d.userName}</bdi>
                  </span>
                  <span className="text-[12px] text-ink-soft">
                    <bdi>{nameOf(d.formId, d.formName)}</bdi>
                  </span>
                </span>
                <span className={`rounded-ui-pill px-[9px] py-[3px] text-[12px] font-bold ${d.status === 'filled' ? 'bg-success-soft text-success' : d.status === 'missed' ? 'bg-danger-soft text-danger' : 'bg-warning-soft text-warning'}`}>
                  {d.status === 'filled' && d.filledAtUtc ? `${statusLabel.filled} · ${time(d.filledAtUtc)}` : statusLabel[d.status === 'pending' ? 'due' : (d.status as DayState)]}
                </span>
              </li>
            ))}
          {dayOpen && days.every((d) => d.date !== dayOpen) && <li className="py-3 text-[14px] text-ink-soft">{isFriday(dayOpen) ? t('assignments.tracking.notWorkingDay') : t('assignments.admin.noDays')}</li>}
        </ul>
      </Sheet>
    </div>
  );
}

/** The assign panel (23): form, users with their Telegram status, from / to, note, "Assign n users". */
function AssignPanel({
  panelRef,
  token,
  users,
  onAssigned,
}: {
  panelRef: React.RefObject<HTMLElement>;
  token: string;
  users: UserSummaryDto[];
  onAssigned: () => void;
}) {
  const { t } = useTranslation();
  const { formName } = useLocalized();
  const { forms } = useShell();
  const published = (forms ?? []).filter((f) => f.status === 'Published');
  const [formId, setFormId] = useState(published[0]?.id ?? '');
  const [userIds, setUserIds] = useState<Set<string>>(new Set());
  const [start, setStart] = useState(saudiTodayIso());
  const [end, setEnd] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useErrorMessage();
  useEffect(() => {
    if (!formId && published[0]) setFormId(published[0].id);
  }, [formId, published]);

  async function assign() {
    setError(null);
    setResult(null);
    if (!formId || userIds.size === 0 || !start) return setError({ key: 'assignments.admin.chooseFormAndUsers' });
    setBusy(true);
    try {
      const r = await api.assignments.create(token, { formDefinitionId: formId, userIds: [...userIds], startDate: start, endDate: end, note: note.trim() || null });
      setResult([t('assignments.admin.created', { n: r.created }), r.alreadyAssigned.length ? t('assignments.admin.alreadyAssigned', { names: r.alreadyAssigned.join('، ') }) : ''].filter(Boolean).join(' '));
      setUserIds(new Set());
      setNote('');
      onAssigned();
    } catch (err) {
      setError({ err, fallbackKey: 'assignments.admin.assignError' });
    } finally {
      setBusy(false);
    }
  }

  return (
    <aside ref={panelRef} aria-label={t('assignments.admin.newTitle')} className="flex max-w-[380px] flex-[1_1_320px] scroll-mt-6 flex-col gap-[14px] rounded-ui-2xl border border-border bg-panel p-[18px]">
      <h2 className="text-[17px] font-extrabold text-ink">{t('assignments.admin.newTitle')}</h2>
      <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
        {t('assignments.admin.formLabel')}
        <select className={builderInput} value={formId} onChange={(e) => setFormId(e.target.value)}>
          {published.map((f) => (
            <option key={f.id} value={f.id}>
              {formName(f)}
            </option>
          ))}
        </select>
      </label>
      <fieldset className="flex flex-col gap-1.5">
        <legend className="mb-1.5 text-[14px] font-bold text-ink">{t('assignments.admin.usersLabel')}</legend>
        {users.map((u) => {
          return (
            <label key={u.id} className="flex min-h-[44px] items-center gap-[10px] rounded-ui-md border border-divider px-[10px] text-[14px]">
              <input type="checkbox" className="h-[18px] w-[18px] accent-[var(--accent)]" checked={userIds.has(u.id)} onChange={() => setUserIds((p) => { const n = new Set(p); if (n.has(u.id)) n.delete(u.id); else n.add(u.id); return n; })} />
              <span className="flex-1 font-bold text-ink">
                <bdi>{u.displayName}</bdi>
              </span>
              <span className="flex flex-col items-end text-[12px] leading-tight">
                <span className={u.telegramLinked ? 'text-success' : 'text-warning'}>{u.telegramLinked ? t('usersV2.remindersTelegram') : t('usersV2.noTelegram')}</span>
                <span className={u.phoneNumber ? 'text-success' : 'text-ink-soft'}>{u.phoneNumber ? t('whatsapp.channelOk') : t('whatsapp.channelNone')}</span>
              </span>
            </label>
          );
        })}
      </fieldset>
      <div className="grid grid-cols-2 gap-[10px]">
        <div className="flex flex-col gap-1.5">
          <span className="text-[14px] font-bold text-ink">{t('assignmentsV2.fromLabel')}</span>
          <DateButton value={start} onPick={setStart} title={t('assignmentsV2.fromLabel')} className="h-[42px]" />
        </div>
        <div className="flex flex-col gap-1.5">
          <span className="text-[14px] font-bold text-ink">{t('assignmentsV2.toLabel')}</span>
          <DateButton value={end} onPick={(iso) => setEnd(iso < start ? start : iso)} title={t('assignmentsV2.toLabel')} placeholder={t('assignments.admin.noEnd')} className="h-[42px]" />
          {end && (
            <button type="button" onClick={() => setEnd(null)} className="self-start text-[12px] text-accent">
              {t('assignments.admin.noEnd')}
            </button>
          )}
        </div>
      </div>
      <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
        {t('assignments.admin.note')}
        <input className={`${builderInput} !h-[42px] text-[14px] font-normal`} value={note} onChange={(e) => setNote(e.target.value)} />
      </label>
      {error && <p className="text-[14px] text-danger">{error}</p>}
      {result && (
        <p className="text-[14px] text-success" role="status">
          {result}
        </p>
      )}
      <Button size="desktop" block disabled={busy} onClick={assign} className="!h-[46px] !text-[16px]">
        {busy ? t('assignments.admin.assigning') : t('assignmentsV2.assignUsers', { count: userIds.size })}
      </Button>
    </aside>
  );
}
