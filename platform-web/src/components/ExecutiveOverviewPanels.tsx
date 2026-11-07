import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { ExecutiveOverviewDto, OverviewKpiTone, OverviewPhase, OverviewProjectDto, OverviewStockMaterialDto } from '../types';

// The dashboard is drawn on a fixed 1760x990 stage scaled to fit the window (routes/
// ExecutiveOverview.tsx), so nothing here uses a responsive (sm:/lg:) class. The redesign was
// drawn at 1600x900 (HANDOFF 25); every size here is that design's value x1.1 so it looks the
// same once fitted (owner decision 5, 2026-11-06).

/**
 * Number and date formatting for the dashboard, in the active language. Arabic keeps Latin
 * digits (the "-u-nu-latn" extension) so figures here match every other number in the app.
 */
export function useOverviewFormat() {
  const { i18n } = useTranslation();
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  const percentFormat = new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 });
  const numberFormat = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
  const dayFormat = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' });
  const monthFormat = new Intl.DateTimeFormat(locale, { month: 'short', timeZone: 'UTC' });
  const dateFormat = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
  return {
    percent: (share: number) => percentFormat.format(share),
    number: (value: number) => numberFormat.format(value),
    /** "2026-10-10" -> "10 Oct". Parsed and printed as UTC so the calendar date never shifts. */
    day: (isoDate: string) => dayFormat.format(new Date(`${isoDate}T00:00:00Z`)),
    month: (isoDate: string) => monthFormat.format(new Date(`${isoDate}T00:00:00Z`)),
    /** "2027-01-06" -> "6 Jan 2027", for dates that can fall in another year. */
    date: (isoDate: string) => dateFormat.format(new Date(`${isoDate}T00:00:00Z`)),
    /** Whole percentage points, always with a sign. */
    points: (value: number) => `${value > 0 ? '+' : value < 0 ? '−' : ''}${numberFormat.format(Math.abs(value))}`,
  };
}

/** A titled card (25): 15px radius, 15/18px padding, 16.5px/800 title, 13px caption; the body fills the rest and clips. */
export function Panel({ title, caption, aside, children }: { title: ReactNode; caption?: string; aside?: ReactNode; children: ReactNode }) {
  return (
    <section className="flex h-full min-h-0 min-w-0 flex-col gap-[11px] rounded-[15px] border border-border bg-panel px-[18px] py-[15px]">
      <div className="flex shrink-0 items-baseline justify-between gap-3">
        <h2 className="shrink-0 text-[16.5px] font-extrabold text-ink">{title}</h2>
        {aside && (
          <span className="min-w-0 truncate text-[13px] text-ink-soft" title={aside as string}>
            {aside}
          </span>
        )}
      </div>
      {caption && (
        <p className="-mt-1.5 line-clamp-2 shrink-0 text-[13px] leading-tight text-ink-soft" title={caption}>
          {caption}
        </p>
      )}
      <div className="min-h-0 flex-1">{children}</div>
    </section>
  );
}

/** The dashed empty state (25): a title and what will fill the panel. */
export function EmptyBox({ title, body }: { title: string; body?: string }) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-[7px] rounded-[13px] border-[1.5px] border-dashed border-border p-[13px] text-center">
      <span className="text-[15.4px] font-bold text-ink">{title}</span>
      {body && <span className="text-[13px] text-ink-soft">{body}</span>}
    </div>
  );
}

// ------------------------------------------------------------------------------------ tiles

/** Value colour (25): --success for good, --danger for late/behind, --ink otherwise. No side borders. */
const TONE_VALUE: Record<OverviewKpiTone, string> = { good: 'text-success', warn: 'text-ink', bad: 'text-danger' };

function Tile({ label, value, detail, tone, title }: { label: string; value: ReactNode; detail: ReactNode; tone?: OverviewKpiTone | null; title?: string }) {
  return (
    <div title={title} className="flex min-h-[119px] min-w-0 flex-col gap-[4.4px] rounded-[15px] border border-border bg-panel px-[15px] py-[13px]">
      <span className="line-clamp-2 text-[13.2px] font-medium leading-tight text-ink-soft">{label}</span>
      <span className={`truncate text-[30.8px] font-extrabold leading-[1.15] ${tone ? TONE_VALUE[tone] : 'text-ink'}`}>{value}</span>
      <span className="line-clamp-2 text-[13.2px] leading-[1.4] text-ink-soft">{detail}</span>
    </div>
  );
}

export function OverviewTiles({ data, selected, phase }: { data: ExecutiveOverviewDto; selected: OverviewProjectDto | null; phase: OverviewPhase | null }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();
  const tiles = data.tiles;
  const dash = t('common.empty');
  const planDelta =
    tiles.planMetLast14Days !== null && tiles.planMetPrevious14Days !== null
      ? Math.round(tiles.planMetLast14Days * 100) - Math.round(tiles.planMetPrevious14Days * 100)
      : null;
  const forecastDetail =
    tiles.forecastFinish === null
      ? t('executiveOverview.tiles.forecastNone')
      : tiles.forecastTarget === null || tiles.forecastDaysLate === null
        ? t('executiveOverview.tiles.forecastNoTarget')
        : tiles.forecastDaysLate > 0
          ? t('executiveOverview.tiles.forecastLate', { n: tiles.forecastDaysLate })
          : tiles.forecastDaysLate < 0
            ? t('executiveOverview.tiles.forecastEarly', { n: -tiles.forecastDaysLate })
            : t('executiveOverview.tiles.forecastOnTime');
  const forecastTitle = tiles.forecastTarget === null ? undefined : t('executiveOverview.tiles.forecastTarget', { date: format.date(tiles.forecastTarget) });
  const stageName = phase ? t(`executiveOverview.phase.${phase}`) : null;

  return (
    <div className="grid h-full grid-cols-8 gap-[11px]">
      {phase ? (
        <>
          <Tile label={t('executiveOverview.filter.stageLabel')} value={stageName} detail={selected ? <span dir="auto">{selected.code} · {selected.name}</span> : t('executiveOverview.filter.all')} />
          <Tile label={t('executiveOverview.tiles.workComplete')} value={format.percent(tiles.stageWorkComplete ?? 0)} detail={t('executiveOverview.tiles.stageWork', { earned: tiles.stageEarnedDays, planned: tiles.stagePlannedDays })} />
        </>
      ) : selected ? (
        <>
          <Tile
            label={t('executiveOverview.tiles.status')}
            value={t(`executiveOverview.projectStatus.${selected.status}`, selected.status ?? dash)}
            detail={<span dir="auto">{[selected.city, selected.manager].filter(Boolean).join(' · ') || dash}</span>}
          />
          <Tile
            label={t('executiveOverview.tiles.workComplete')}
            value={format.percent(selected.workComplete)}
            detail={
              selected.timeElapsed === null || selected.scheduleGapPoints === null
                ? t('executiveOverview.tiles.noSchedule')
                : t('executiveOverview.tiles.againstTime', { elapsed: format.percent(selected.timeElapsed), points: format.points(selected.scheduleGapPoints) })
            }
          />
        </>
      ) : (
        <>
          <Tile label={t('executiveOverview.tiles.projects')} value={format.number(tiles.projectCount)} detail={t('executiveOverview.tiles.projectsDetail', { active: tiles.activeCount, onHold: tiles.onHoldCount, completed: tiles.completedCount })} />
          <Tile label={t('executiveOverview.tiles.workComplete')} value={format.percent(tiles.workComplete)} detail={t('executiveOverview.tiles.workCompleteDetail', { n: tiles.activeBehindCount })} />
        </>
      )}
      <Tile
        label={t('executiveOverview.tiles.planMet')}
        value={tiles.planMetLast14Days === null ? dash : format.percent(tiles.planMetLast14Days)}
        tone={tiles.planMetLast14Days === null ? null : tiles.planMetLast14Days >= 0.75 ? 'good' : tiles.planMetLast14Days < 0.4 ? 'bad' : null}
        detail={
          tiles.planMetLast14Days === null ? (
            t('executiveOverview.tiles.noReports14')
          ) : planDelta === null ? (
            t('executiveOverview.tiles.reportCount', { n: tiles.reportsLast14Days })
          ) : (
            <>
              {planDelta >= 0 ? '▲' : '▼'} {t('executiveOverview.points', { points: format.number(Math.abs(planDelta)) })} {t('executiveOverview.tiles.againstPrevious14')}
            </>
          )
        }
      />
      <Tile
        label={t('executiveOverview.tiles.workers')}
        value={tiles.workersOnSite === null ? dash : format.number(tiles.workersOnSite)}
        detail={tiles.workersOnSiteDate === null ? t('executiveOverview.tiles.noReports') : t('executiveOverview.tiles.workersDetail', { date: format.day(tiles.workersOnSiteDate) })}
      />
      <Tile
        label={t('executiveOverview.tiles.delays')}
        value={tiles.delayedShareLast30Days === null ? dash : format.percent(tiles.delayedShareLast30Days)}
        detail={tiles.delayedShareLast30Days === null ? t('executiveOverview.tiles.noReports30') : t('executiveOverview.tiles.delaysDetail', { delayed: tiles.delayedReportsLast30Days, total: tiles.reportsLast30Days })}
      />
      <Tile
        label={stageName ? `${t('executiveOverview.tiles.forecast')} · ${stageName}` : t('executiveOverview.tiles.forecast')}
        value={tiles.forecastFinish === null ? dash : format.date(tiles.forecastFinish)}
        detail={[tiles.forecastFinished ? t('executiveOverview.tiles.forecastFinished') : null, forecastDetail, forecastTitle].filter(Boolean).join(' · ')}
        tone={tiles.forecastTone}
        title={forecastTitle}
      />
      <Tile label={t('executiveOverview.tiles.lateDays')} value={tiles.workedDays === 0 ? dash : format.number(tiles.lateDays)} detail={t('executiveOverview.tiles.lateDaysDetail')} tone={tiles.lateDaysTone} />
      <Tile
        label={t('executiveOverview.tiles.lateShare')}
        value={tiles.lateShare === null ? dash : format.percent(tiles.lateShare)}
        detail={t('executiveOverview.tiles.lateShareDetail', { late: tiles.lateDays, worked: tiles.workedDays })}
        tone={tiles.lateShareTone}
      />
    </div>
  );
}

// -------------------------------------------------------------------------- project table

const PROJECT_COLUMNS = '1.1fr 0.8fr 0.9fr 2fr 1.4fr 0.9fr 0.7fr 1fr';
const STATUS_COLOUR: Record<string, string> = { active: 'text-success', on_hold: 'text-danger', completed: 'text-accent', planning: 'text-ink-soft', cancelled: 'text-ink-soft' };
/** A project that isn't finished and hasn't reported for longer than this is called out. */
const QUIET_AFTER_DAYS = 3;

/**
 * Projects (25): work complete as an --accent bar on --border with a 2px tick for time elapsed,
 * five phase segments, the schedule in --danger when behind, the last report in --danger with
 * "No report for n days" when quiet. A row selects (or clears) the project filter.
 */
export function ProjectsTable({ projects, selectedId, onSelect }: { projects: OverviewProjectDto[]; selectedId: string | null; onSelect: (projectId: string | null) => void }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();
  const dash = t('common.empty');
  if (projects.length === 0) return <EmptyBox title={t('executiveOverview.projects.empty')} />;

  return (
    <div className="flex h-full flex-col">
      <div className="grid gap-[9px] border-b border-border pb-[9px] text-[13.2px] text-ink-soft" style={{ gridTemplateColumns: PROJECT_COLUMNS }}>
        {(['project', 'status', 'nowIn', 'workComplete', 'phases', 'schedule', 'planMet', 'lastReport'] as const).map((k) => (
          <span key={k} className="truncate">
            {t(`executiveOverview.projects.${k}`)}
          </span>
        ))}
      </div>
      <div className="min-h-0 flex-1 overflow-auto">
        {projects.map((project) => {
          const isSelected = project.id === selectedId;
          const finished = project.scheduleStatus === 'finished';
          const quiet = !finished && project.daysSinceLastReport > QUIET_AFTER_DAYS;
          const toggle = () => onSelect(isSelected ? null : project.id);
          return (
            <div
              key={project.id}
              role="button"
              tabIndex={0}
              aria-pressed={isSelected}
              onClick={toggle}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  toggle();
                }
              }}
              className={`grid cursor-pointer items-center gap-[9px] border-b border-divider py-[9px] text-[15.4px] text-ink outline-none last:border-b-0 hover:bg-accent-row focus-visible:bg-accent-row ${isSelected ? 'bg-accent-row' : ''}`}
              style={{ gridTemplateColumns: PROJECT_COLUMNS }}
            >
              <span className="flex min-w-0 flex-col">
                <span className="truncate font-extrabold" dir="auto" title={project.name}>
                  {project.name}
                </span>
                <span className="truncate text-[13.2px] text-ink-soft">
                  {project.code}
                  {project.city && (
                    <>
                      {' · '}
                      <span dir="auto">{project.city}</span>
                    </>
                  )}
                </span>
              </span>
              <span className={`truncate text-[14.3px] font-bold ${STATUS_COLOUR[project.status ?? ''] ?? 'text-ink-soft'}`}>
                {project.status ? `● ${t(`executiveOverview.projectStatus.${project.status}`, project.status)}` : dash}
              </span>
              <span className="truncate">{finished || project.currentPhases.length === 0 ? dash : project.currentPhases.map((p) => t(`executiveOverview.phase.${p}`, p)).join(' + ')}</span>
              <span className="flex items-center gap-[9px]">
                <span className="font-extrabold">{format.percent(project.workComplete)}</span>
                <span
                  aria-hidden="true"
                  className="relative h-[9px] flex-1 rounded-ui-pill bg-border"
                  title={project.timeElapsed === null ? undefined : t('executiveOverview.projects.barTitle', { work: format.percent(project.workComplete), elapsed: format.percent(project.timeElapsed) })}
                >
                  <span className="absolute inset-y-0 start-0 rounded-ui-pill bg-accent" style={{ width: `${Math.round(project.workComplete * 100)}%` }} />
                  {project.timeElapsed !== null && <span className="absolute -top-[4.4px] h-[17.6px] w-[2px] bg-ink" style={{ insetInlineStart: `calc(${Math.round(project.timeElapsed * 100)}% - 1px)` }} />}
                </span>
              </span>
              <span className="grid grid-cols-5 gap-[3px]" role="img" aria-label={project.phases.map(({ phase, complete }) => `${t(`executiveOverview.phase.${phase}`, phase)} ${format.percent(complete)}`).join(', ')}>
                {project.phases.map(({ phase, complete }) => (
                  <span
                    key={phase}
                    title={`${t(`executiveOverview.phase.${phase}`, phase)} ${format.percent(complete)}`}
                    className="h-[9px] rounded-[3px]"
                    style={{ background: `linear-gradient(var(--grad-dir, 90deg), var(--accent) ${Math.round(complete * 100)}%, var(--border) ${Math.round(complete * 100)}%)` }}
                  />
                ))}
              </span>
              <span className={`truncate text-[14.3px] font-bold ${project.scheduleStatus === 'behind' ? 'text-danger' : project.scheduleStatus === 'ahead' ? 'text-success' : 'text-ink-soft'}`}>
                {finished
                  ? t('executiveOverview.schedule.finished', { date: format.day(project.lastReportDate) })
                  : project.scheduleStatus === 'ahead' || project.scheduleStatus === 'behind'
                    ? t(`executiveOverview.schedule.${project.scheduleStatus}`, { points: format.points(project.scheduleGapPoints ?? 0) })
                    : t(`executiveOverview.schedule.${project.scheduleStatus}`)}
              </span>
              <span className="font-bold">{format.percent(project.planMet)}</span>
              <span className="flex min-w-0 flex-col">
                <span className={`font-bold ${quiet ? 'text-danger' : ''}`}>{format.day(project.lastReportDate)}</span>
                {quiet && <span className="truncate text-[13.2px] text-ink-soft">{t('executiveOverview.projects.quiet', { n: project.daysSinceLastReport })}</span>}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ----------------------------------------------------------------------------------- charts

export interface WeekBar {
  weekStart: string;
  /** null = no reports that week (a hairline, not a bar). */
  value: number | null;
  tooltip: string;
}

/**
 * Weekly bars (25: "Plan met, by week" replaces the line chart; "Workers on site, by week").
 * colour(value) picks each bar's colour; months (or dates) are labelled under the bars.
 */
export function WeekBars({ weeks, max, colour, ariaLabel, emptyText, labelEvery = 'month' }: { weeks: WeekBar[]; max: number; colour: (value: number) => string; ariaLabel: string; emptyText: string; labelEvery?: 'month' | 'thirds' }) {
  const format = useOverviewFormat();
  if (weeks.every((w) => w.value === null)) return <EmptyBox title={emptyText} />;
  const labels =
    labelEvery === 'month'
      ? weeks.filter((w, i) => i === 0 || format.month(w.weekStart) !== format.month(weeks[i - 1].weekStart)).map((w) => format.month(w.weekStart))
      : [weeks[0], weeks[Math.floor(weeks.length / 2)], weeks[weeks.length - 1]].map((w) => format.day(w.weekStart));
  return (
    <div className="flex h-full flex-col gap-[7px]">
      <div role="img" aria-label={ariaLabel} className="flex min-h-0 flex-1 items-end gap-[3px] border-b border-border pt-[7px]">
        {weeks.map((w) => (
          <span
            key={w.weekStart}
            title={w.tooltip}
            className="min-h-[2px] flex-[1_1_0] rounded-t-[3px]"
            style={{ height: w.value === null ? 2 : `${Math.max(0, Math.min(1, w.value / max)) * 100}%`, background: w.value === null ? 'var(--border)' : colour(w.value) }}
          />
        ))}
      </div>
      <div className="flex justify-between text-[12.1px] text-ink-soft">
        {labels.map((l, i) => (
          <span key={`${l}-${i}`}>{l}</span>
        ))}
      </div>
    </div>
  );
}

/** Plan met by week (25): >= 75% --accent, 40-74% a muted accent, < 40% --danger. */
export function planColour(percent: number) {
  return percent >= 75 ? 'var(--accent)' : percent >= 40 ? 'color-mix(in srgb, var(--accent) 55%, var(--border))' : 'var(--danger)';
}

export interface HBar {
  label: string;
  value: number;
  text: string;
  title?: string;
  colour: string;
  /** Arabic text kept right-to-left even in English (delay causes are typed in Arabic). */
  rtl?: boolean;
}

/** Horizontal bars (25: plan met by weather, why days were delayed). */
export function HBars({ items, max, emptyText, thick = false }: { items: HBar[]; max: number; emptyText: string; thick?: boolean }) {
  if (items.length === 0) return <EmptyBox title={emptyText} />;
  return (
    <div className="flex flex-col gap-[7px]">
      {items.map((item) => (
        <div key={item.label} className="flex flex-col gap-[4.4px]" title={item.title}>
          <div className="flex justify-between gap-[11px] text-[14.3px] text-ink">
            <span className="truncate" dir={item.rtl ? 'rtl' : undefined} lang={item.rtl ? 'ar' : undefined}>
              {item.label}
            </span>
            <span className="font-extrabold">{item.text}</span>
          </div>
          <div aria-hidden="true" className={`rounded-ui-pill bg-border ${thick ? 'h-[11px]' : 'h-[9px]'}`}>
            <div className={`rounded-ui-pill ${thick ? 'h-[11px]' : 'h-[9px]'}`} style={{ width: `${Math.max(0, Math.min(1, item.value / max)) * 100}%`, background: item.colour }} />
          </div>
        </div>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------------- stock

/** How many materials the stock panel has room for; the rest are counted in its caption. */
export const STOCK_ROWS_SHOWN = 8;

export function StockTable({ materials }: { materials: OverviewStockMaterialDto[] }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();
  if (materials.length === 0) return <EmptyBox title={t('executiveOverview.stock.empty')} body={t('overviewV2.stockEmptyHint')} />;
  return (
    <div className="h-full overflow-auto">
      <table className="w-full border-collapse text-[14.3px]">
        <thead>
          <tr className="text-[13.2px] text-ink-soft">
            <th className="border-b border-border pb-[7px] text-start font-normal">{t('executiveOverview.stock.material')}</th>
            <th className="border-b border-border pb-[7px] text-end font-normal">{t('executiveOverview.stock.issued')}</th>
            <th className="border-b border-border pb-[7px] text-end font-normal">{t('executiveOverview.stock.onHand')}</th>
            <th className="border-b border-border pb-[7px] text-end font-normal">{t('executiveOverview.stock.issuedShare')}</th>
          </tr>
        </thead>
        <tbody>
          {materials.slice(0, STOCK_ROWS_SHOWN).map((m) => (
            <tr key={`${m.isCustomMaterial}-${m.material}`} className="border-b border-divider last:border-b-0">
              <td className="max-w-[132px] truncate py-[5px]">
                <span dir="auto" className="text-ink">
                  {/* A material typed into the "other" box has no translation - it is shown as written. */}
                  {m.isCustomMaterial ? m.material : t(`executiveOverview.material.${m.material}`, m.material)}
                </span>{' '}
                {m.unit && <span className="text-[12.1px] text-ink-soft">{t(`executiveOverview.unit.${m.unit}`, m.unit)}</span>}
              </td>
              <td className="py-[5px] text-end font-bold text-ink">{format.number(m.issued)}</td>
              <td className="py-[5px] text-end text-ink">{format.number(m.onHand)}</td>
              <td className="py-[5px] text-end text-ink-soft">{format.percent(m.issuedShare)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ------------------------------------------------------------------------------- problems

/** How many of the latest problems the panel has room for. */
export const PROBLEMS_SHOWN = 4;

export function ProblemsList({ problems, since }: { problems: ExecutiveOverviewDto['latestProblems']; since: string }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();
  if (problems.length === 0) return <EmptyBox title={t('executiveOverview.problems.empty')} body={since ? t('overviewV2.problemsSince', { date: since }) : undefined} />;
  return (
    <ul className="flex h-full flex-col overflow-hidden">
      {problems.slice(0, PROBLEMS_SHOWN).map((problem, index) => (
        <li key={index} className="flex flex-col gap-0.5 border-b border-divider py-[7px] last:border-b-0">
          <span className="flex justify-between gap-2 text-[12.1px] text-ink-soft">
            <span>{format.day(problem.date)}</span>
            <span className="truncate">{t(`executiveOverview.phase.${problem.phase}`, problem.phase)}</span>
          </span>
          <span dir="auto" className="line-clamp-2 text-[14.3px] leading-snug text-ink" title={problem.description}>
            {problem.description}
          </span>
          <span dir="auto" className="truncate text-[12.1px] text-ink-soft">
            {problem.projectName}
          </span>
        </li>
      ))}
    </ul>
  );
}
