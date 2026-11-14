import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ContactLink, userContactUrl } from '../components/ContactLink';
import { api } from '../api/client';
import { builderInput } from '../components/builder/FieldSettings';
import { DateButton } from '../components/DateField';
import { useShell } from '../components/shell/ShellData';
import { Icon } from '../components/icons';
import { Button, DayCell, DayLegend, EmptyState, ErrorState, LoadingState, Sheet, useIsDesktop } from '../components/ui';
import { apiErrorMessage } from '../api/errorMessage';
import type { DayState } from '../components/ui';
import { saudiTodayIso } from '../forms/fieldValues';
import { useLocalized } from '../forms/localized';
import { dayLabel, localeFor } from '../forms/recordAnswers';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { SetupComplete, SetupStrip, useSetupProject, useSetupSteps } from '../components/ProjectSetup';
import { preselectUsers, SETUP_FORMS } from '../nav/projectSetup';
import type { AssignmentDayDto, FormAssignmentDto, FormSummaryDto, UserSummaryDto } from '../types';

const addDays = (iso: string, n: number) => new Date(Date.parse(`${iso}T00:00:00Z`) + n * 86_400_000).toISOString().slice(0, 10);
const isFriday = (iso: string) => new Date(`${iso}T00:00:00Z`).getUTCDay() === 5;
/** The Saturday that starts the Saudi working week holding `iso`. */
const weekStartOf = (iso: string) => addDays(iso, -((new Date(`${iso}T00:00:00Z`).getUTCDay() + 1) % 7));

/**
 * Form assignments + daily tracking (screen 23), one page (the old "by day" page redirects here):
 * - Today card: "n of m filled", Filled / Due today / Not filled pills, and "Remind those who
 *   haven't filled (n)" - the existing sender, manual reminders once per person per day.
 * - "Who filled and when": one row per assignment, one cell per day (Fridays greyed), Rate = filled
 *   / due; a filled cell opens that entry; a day header lists who was due that day. Form filter,
 *   "Show ended". One Saturday-Friday week at a time on every screen (owner, 2026-11-13), the
 *   current one first, with arrows for the previous and next weeks - no sideways scrolling.
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
  const [weekOffset, setWeekOffset] = useState(0); // weeks back from the current one
  const [formFilter, setFormFilter] = useState('');
  const [showEnded, setShowEnded] = useState(false);
  const [tick, setTick] = useState(0);
  const [error, setError] = useErrorMessage();
  const [reminding, setReminding] = useState(false);
  const [remindResult, setRemindResult] = useState<string | null>(null);
  const [dayOpen, setDayOpen] = useState<string | null>(null);
  const panel = useRef<HTMLElement>(null);
  // Admin flows F3-5: /admin/assignments?assign={userId} opens the panel with that person ticked.
  const [params] = useSearchParams();
  const assignParam = params.get('assign');
  const initialUserIds = useMemo(() => (assignParam ? [assignParam] : undefined), [assignParam]);
  // Admin flows F2-6/F2-7: the last project setup step (?setup={projectId}) - the daily report
  // selected, the strip's "Finish setup"; Assign or Finish ends the chain.
  const setupProject = useSetupProject(params.get('setup'));
  const setupStepsList = useSetupSteps();
  const [setupDone, setSetupDone] = useState(false);
  useEffect(() => {
    // On a phone the panel sits below the grid: bring it into view once the people are loaded.
    if ((assignParam || setupProject) && !desktop && users.length > 0) panel.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, [assignParam, setupProject, desktop, users.length]);

  const weekStart = addDays(weekStartOf(today), -7 * weekOffset);
  const weekEnd = addDays(weekStart, 6);
  const from = weekStart;
  const to = weekEnd > today ? today : weekEnd;
  useEffect(() => {
    Promise.all([api.assignments.list(token), api.users.list(token)])
      .then(([a, u]) => {
        setAssignments(a);
        setUsers(u.filter((x) => x.isActive));
      })
      .catch((err) => setError({ err, fallbackKey: 'assignments.admin.loadError' }));
    setDaysError(null);
    api.assignments
      .days(token, { from, to })
      .then(setDays)
      .catch((err) => (setDays([]), setDaysError(err)));
    api.assignments
      .days(token, { from: today, to: today })
      .then(setTodayDays)
      .catch((err) => (setTodayDays([]), setDaysError(err)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, from, to, today, tick]);

  const dates = useMemo(() => Array.from({ length: 7 }, (_, i) => addDays(weekStart, i)), [weekStart]);
  const byCell = useMemo(() => new Map(days.map((d) => [`${d.assignmentId}|${d.date}`, d])), [days]);
  const rows = (assignments ?? [])
    .filter((a) => (showEnded || a.isActive) && (!formFilter || a.formId === formFilter))
    .filter((a) => a.startDate <= to && (!a.endDate || a.endDate >= from))
    .sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.userName.localeCompare(b.userName));
  const filled = todayDays.filter((d) => d.status === 'filled').length;
  const pending = todayDays.filter((d) => d.status === 'pending');
  const lastReminded = pending
    .map((d) => d.lastRemindedAtUtc)
    .filter(Boolean)
    .sort()
    .pop();
  const time = (utc: string) =>
    new Intl.DateTimeFormat(locale, {
      hour: 'numeric',
      minute: '2-digit',
      timeZone: 'Asia/Riyadh',
    }).format(new Date(utc));
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

  /** One assignment's cells for `dates`, with its filled / due count. */
  const cellsFor = (a: FormAssignmentDto, height: number) => {
    let due = 0;
    let done = 0;
    const cells = dates.map((d) => {
      const day = byCell.get(`${a.id}|${d}`);
      const state: DayState = !day || isFriday(d) ? 'off' : day.status === 'filled' ? 'filled' : day.status === 'missed' ? 'missed' : 'due';
      if (state === 'filled' || state === 'missed') due++;
      if (state === 'filled') done++;
      const label =
        t('assignmentsV2.cellLabel', {
          user: a.userName,
          date: dayLabel(d, locale),
          status: statusLabel[state],
        }) + (day?.lastRemindedAtUtc ? ` · ${t('assignmentsV2.remindedAt', { time: time(day.lastRemindedAtUtc) })}` : '');
      return <DayCell key={d} state={state} size="fill" height={height} label={label} onClick={day?.recordId ? () => navigate(`/entries/${a.formId}/${day.recordId}`) : undefined} />;
    });
    return { cells, done, due };
  };
  const shortDay = (d: string, width: 'narrow' | 'short') => new Intl.DateTimeFormat(locale, { weekday: width, timeZone: 'UTC' }).format(new Date(`${d}T00:00:00Z`));
  const weekLabel = `${new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${weekStart}T00:00:00Z`))} – ${new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${weekEnd}T00:00:00Z`))}`;

  /** Previous / next week (owner, 2026-11-13: a fixed Saturday-Friday week on every screen). */
  const weekNav = (
    <div className="flex items-center justify-between gap-2">
      <button
        type="button"
        onClick={() => setWeekOffset((w) => w + 1)}
        aria-label={t('assignmentsV2.prevWeek')}
        className="flex h-11 w-11 items-center justify-center rounded-full border border-border bg-panel text-ink"
      >
        <Icon name="chevronBack" size={20} />
      </button>
      <div className="flex flex-col items-center text-center">
        <span className="text-[15px] font-bold text-ink" dir="auto">
          {weekLabel}
        </span>
        {weekOffset === 0 ? (
          <span className="text-[12px] text-ink-soft">{t('assignmentsV2.thisWeek')}</span>
        ) : (
          <button type="button" onClick={() => setWeekOffset(0)} className="text-[12px] font-bold text-accent-strong underline">
            {t('assignmentsV2.backToThisWeek')}
          </button>
        )}
      </div>
      <button
        type="button"
        disabled={weekOffset === 0}
        onClick={() => setWeekOffset((w) => Math.max(0, w - 1))}
        aria-label={t('assignmentsV2.nextWeek')}
        className="flex h-11 w-11 items-center justify-center rounded-full border border-border bg-panel text-ink disabled:opacity-30"
      >
        <Icon name="chevronForward" size={20} />
      </button>
    </div>
  );

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
      setRemindResult(
        t('assignmentsV2.remindResult', {
          sent: r.sent,
          failed: r.failed,
          already: r.alreadyReminded,
          none: r.noChannel,
        }),
      );
      setTick((x) => x + 1);
    } catch (err) {
      setError({ err, fallbackKey: 'assignments.admin.loadError' });
    } finally {
      setReminding(false);
    }
  }

  if (!assignments) return error ? <ErrorState message={error} onRetry={() => (setError(null), setTick((x) => x + 1))} /> : <LoadingState />;

  // On a phone the cells are 44px tap targets and the grid scrolls sideways inside its card.
  const grid = `210px repeat(${dates.length}, minmax(0, 1fr)) 92px`;
  return (
    <div className="flex flex-col gap-[18px]">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-[14px] text-ink-soft">{t('nav.administration')}</span>
          <h1 className="text-[28px] font-extrabold text-ink">{t('assignments.admin.title')}</h1>
          <p className="text-[14px] text-ink-soft">{t('assignmentsV2.rule')}</p>
        </div>
        <Button
          size="desktop"
          onClick={() =>
            panel.current?.scrollIntoView({
              behavior: 'smooth',
              block: 'start',
            })
          }
        >
          {t('assignmentsV2.assignForm')}
        </Button>
      </div>
      {error && <p className="text-[14px] text-danger">{error}</p>}

      <div className="flex flex-wrap items-start gap-4">
        <div className="flex min-w-0 flex-[999_1_560px] flex-col gap-4">
          {/* Today */}
          <section className="flex flex-wrap items-center gap-[18px] rounded-ui-2xl border border-border bg-panel px-[18px] py-4">
            <div className="flex flex-col gap-0.5">
              <span className="text-[13px] text-ink-soft">
                {t('assignmentsV2.todayLabel', {
                  date: dayLabel(today, locale),
                })}
              </span>
              <span className="text-[18px] font-extrabold text-ink">
                {t('assignmentsV2.filledOfTotal', {
                  filled,
                  total: todayDays.length,
                })}
              </span>
            </div>
            <div className="flex flex-wrap gap-2">
              <span className="rounded-ui-pill bg-success-soft px-[11px] py-[5px] text-[13px] font-bold text-success">{t('assignmentsV2.pillFilled', { count: filled })}</span>
              <span className="rounded-ui-pill bg-warning-soft px-[11px] py-[5px] text-[13px] font-bold text-warning">{t('assignmentsV2.pillDue', { count: pending.length })}</span>
              <span className="rounded-ui-pill bg-divider px-[11px] py-[5px] text-[13px] font-bold text-ink-soft">
                {t('assignmentsV2.pillMissed', {
                  count: todayDays.filter((d) => d.status === 'missed').length,
                })}
              </span>
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
              <h2 className="text-[17px] font-extrabold text-ink">{t('assignmentsV2.gridTitleWeek')}</h2>
              <div className="flex flex-wrap items-center gap-1.5">
                <select
                  aria-label={t('assignments.admin.allForms')}
                  value={formFilter}
                  onChange={(e) => setFormFilter(e.target.value)}
                  className="h-[36px] rounded-ui-pill border border-border bg-panel px-3 text-[13px] text-ink"
                >
                  <option value="">{t('assignments.admin.allForms')}</option>
                  {(forms ?? [])
                    .filter((f) => f.status === 'Published')
                    .map((f: FormSummaryDto) => (
                      <option key={f.id} value={f.id}>
                        {formName(f)}
                      </option>
                    ))}
                </select>
                <label className="flex h-[36px] items-center gap-1.5 rounded-ui-pill border border-border px-3 text-[13px] text-ink">
                  <input type="checkbox" checked={showEnded} onChange={(e) => setShowEnded(e.target.checked)} />
                  {t('assignmentsV2.showEnded')}
                </label>
              </div>
            </div>
            {daysError ? (
              <ErrorState message={apiErrorMessage(daysError, t, 'assignments.admin.loadError')} onRetry={() => setTick((x) => x + 1)} />
            ) : !desktop ? (
              <div className="flex flex-col gap-3">
                {weekNav}
                <div className="grid grid-cols-7 gap-1 text-center text-[11px] text-ink-soft">
                  {dates.map((d) => (
                    <button
                      key={d}
                      type="button"
                      onClick={() => setDayOpen(d)}
                      className={`flex min-h-[44px] flex-col items-center justify-end leading-[1.3] ${d === today ? 'font-bold text-ink' : ''}`}
                      aria-label={t('assignmentsV2.dayList', {
                        date: dayLabel(d, locale),
                      })}
                    >
                      <span className="font-bold">{shortDay(d, 'short')}</span>
                      <span>{Number(d.slice(8))}</span>
                    </button>
                  ))}
                </div>
                {rows.map((a) => {
                  const { cells, done, due } = cellsFor(a, 40);
                  return (
                    <div key={a.id} className={`flex flex-col gap-1.5 border-t border-divider pt-3 ${a.isActive ? '' : 'opacity-50'}`}>
                      <div className="flex items-start justify-between gap-2">
                        <span className="flex min-w-0 flex-col">
                          <span className="break-words text-[14px] font-bold text-ink">
                            <bdi>{a.userName}</bdi>
                          </span>
                          <span className="flex flex-wrap items-center gap-x-1.5 text-[12px] text-ink-soft">
                            <bdi>{nameOf(a.formId, a.formName)}</bdi>
                            {!a.isActive && <span>· {t('assignmentsV2.rowEnded')}</span>}
                            {a.isActive && (
                              <button type="button" onClick={() => void stop(a.id)} className="text-danger hover:underline">
                                {t('assignments.admin.stop')}
                              </button>
                            )}
                          </span>
                        </span>
                        <span className="shrink-0 text-[13px] font-bold text-ink" dir="ltr">
                          {done} / {due}
                        </span>
                      </div>
                      <div className="grid grid-cols-7 gap-1">{cells}</div>
                    </div>
                  );
                })}
                {rows.length === 0 && <EmptyState title={t('assignments.admin.noAssignments')} body={t('assignmentsV2.emptyBody')} />}
              </div>
            ) : (
              <div className="flex flex-col gap-3">
                {weekNav}
                <div className="flex flex-col gap-1.5">
                  <div className="grid items-end gap-1 text-center text-[11px] text-ink-soft" style={{ gridTemplateColumns: grid }}>
                    <span />
                    {dates.map((d) => (
                      <button
                        key={d}
                        type="button"
                        onClick={() => setDayOpen(d)}
                        className="flex min-h-[44px] flex-col justify-end leading-[1.3] hover:text-ink lg:min-h-0"
                        aria-label={t('assignmentsV2.dayList', {
                          date: dayLabel(d, locale),
                        })}
                      >
                        <span className="font-bold">
                          {new Intl.DateTimeFormat(locale, {
                            weekday: 'short',
                            timeZone: 'UTC',
                          }).format(new Date(`${d}T00:00:00Z`))}
                        </span>
                        <span>{Number(d.slice(8))}</span>
                      </button>
                    ))}
                    <span>{t('assignmentsV2.rate')}</span>
                  </div>
                  {rows.map((a) => {
                    const { cells, done, due } = cellsFor(a, 30);
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
                        {cells}
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
          onAssigned={() => {
            setTick((x) => x + 1);
            if (setupProject) setSetupDone(true);
          }}
          initialUserIds={initialUserIds}
          initialFormCode={setupProject ? SETUP_FORMS.daily : undefined}
          top={
            setupProject &&
            (setupDone ? (
              <SetupComplete project={setupProject} />
            ) : (
              <SetupStrip
                steps={setupStepsList}
                current="people"
                project={setupProject}
                compact
                action={{ label: t('flows.finish'), onClick: () => setSetupDone(true) }}
              />
            ))
          }
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
                <span
                  className={`rounded-ui-pill px-[9px] py-[3px] text-[12px] font-bold ${d.status === 'filled' ? 'bg-success-soft text-success' : d.status === 'missed' ? 'bg-danger-soft text-danger' : 'bg-warning-soft text-warning'}`}
                >
                  {d.status === 'filled' && d.filledAtUtc ? `${statusLabel.filled} · ${time(d.filledAtUtc)}` : statusLabel[d.status === 'pending' ? 'due' : (d.status as DayState)]}
                </span>
              </li>
            ))}
          {dayOpen && days.every((d) => d.date !== dayOpen) && (
            <li className="py-3 text-[14px] text-ink-soft">{isFriday(dayOpen) ? t('assignments.tracking.notWorkingDay') : t('assignments.admin.noDays')}</li>
          )}
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
  initialUserIds,
  initialFormCode,
  top,
}: {
  panelRef: React.RefObject<HTMLElement>;
  token: string;
  users: UserSummaryDto[];
  onAssigned: (created: number) => void;
  /** Ticked when the panel opens (`?assign=`, admin flows F3-5); unknown or deactivated ids are ignored. */
  initialUserIds?: string[];
  /** The form selected when the panel opens (`?setup=`: the daily report, F2-6). */
  initialFormCode?: string;
  /** Shown at the top of the panel (the project setup strip, F2-6). */
  top?: React.ReactNode;
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
  // Preselection from the address, applied once the people (and forms) have loaded.
  const applied = useRef({ users: false, form: false });
  useEffect(() => {
    if (applied.current.users || !initialUserIds?.length || users.length === 0) return;
    applied.current.users = true;
    const known = preselectUsers(initialUserIds, users);
    if (known.length) setUserIds(new Set(known));
  }, [initialUserIds, users]);
  useEffect(() => {
    if (applied.current.form || !initialFormCode || published.length === 0) return;
    applied.current.form = true;
    const wanted = published.find((f) => f.code === initialFormCode);
    if (wanted) setFormId(wanted.id);
  }, [initialFormCode, published]);

  async function assign() {
    setError(null);
    setResult(null);
    if (!formId || userIds.size === 0 || !start) return setError({ key: 'assignments.admin.chooseFormAndUsers' });
    setBusy(true);
    try {
      const r = await api.assignments.create(token, {
        formDefinitionId: formId,
        userIds: [...userIds],
        startDate: start,
        endDate: end,
        note: note.trim() || null,
      });
      setResult(
        [
          t('assignments.admin.created', { n: r.created }),
          r.alreadyAssigned.length
            ? t('assignments.admin.alreadyAssigned', {
                names: r.alreadyAssigned.join('، '),
              })
            : '',
        ]
          .filter(Boolean)
          .join(' '),
      );
      setUserIds(new Set());
      setNote('');
      onAssigned(r.created);
    } catch (err) {
      setError({ err, fallbackKey: 'assignments.admin.assignError' });
    } finally {
      setBusy(false);
    }
  }

  return (
    <aside
      ref={panelRef}
      aria-label={t('assignments.admin.newTitle')}
      className="flex w-full flex-[1_1_320px] scroll-mt-6 lg:w-auto lg:max-w-[380px] flex-col gap-[14px] rounded-ui-2xl border border-border bg-panel p-[18px]"
    >
      {top}
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
              <input
                type="checkbox"
                className="h-[18px] w-[18px] accent-[var(--accent)]"
                checked={userIds.has(u.id)}
                onChange={() =>
                  setUserIds((p) => {
                    const n = new Set(p);
                    if (n.has(u.id)) n.delete(u.id);
                    else n.add(u.id);
                    return n;
                  })
                }
              />
              <span className="flex-1 font-bold text-ink">
                <bdi>{u.displayName}</bdi>
              </span>
              <span className="flex flex-col items-end text-[12px] leading-tight">
                {/* Admin flows F3-1: a missing channel links to the person's account; it never ticks the box. */}
                {u.telegramLinked ? (
                  <span className="text-success">{t('usersV2.remindersTelegram')}</span>
                ) : (
                  <ContactLink to={userContactUrl(u.id, 'telegram', 'assignments')} label={t('flows.connectTelegramFor', { name: u.displayName })} className="text-warning">
                    {t('usersV2.noTelegram')}
                  </ContactLink>
                )}
                {u.phoneNumber ? (
                  <span className="text-success">{t('whatsapp.channelOk')}</span>
                ) : (
                  <ContactLink to={userContactUrl(u.id, 'whatsapp', 'assignments')} label={t('flows.addWhatsappFor', { name: u.displayName })} className="text-ink-soft">
                    {t('whatsapp.channelNone')}
                  </ContactLink>
                )}
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
