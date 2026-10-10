import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { ExecutiveOverviewDto, OverviewProjectDto, OverviewStockMaterialDto } from '../types';
import { StatusLed } from './StatusLed';

// Every panel here is laid out for the dashboard's fixed 16:9 stage (see
// routes/ExecutiveOverview.tsx): it fills the grid cell it is given and never grows past
// it. That is why nothing in this file uses a responsive (sm:/lg:) class - the stage is
// always the same size and is scaled as a whole, so viewport breakpoints would be wrong.

const TABLE_HEAD_CLASS =
  'sticky top-0 z-[1] whitespace-nowrap border-b border-border bg-panel px-1.5 py-1.5 text-start font-display text-label font-semibold uppercase tracking-[0.07em] text-ink-soft';

/**
 * Number and date formatting for the dashboard, in the active language. Arabic keeps Latin
 * digits (the "-u-nu-latn" extension) so figures here match every other number in the app,
 * which are shown as typed.
 */
export function useOverviewFormat() {
  const { i18n } = useTranslation();
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  const percentFormat = new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 });
  const numberFormat = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
  const dayFormat = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' });

  return {
    percent: (share: number) => percentFormat.format(share),
    number: (value: number) => numberFormat.format(value),
    /** "2026-10-10" -> "10 Oct". Parsed and printed as UTC so the calendar date never shifts. */
    day: (isoDate: string) => dayFormat.format(new Date(`${isoDate}T00:00:00Z`)),
    /** Whole percentage points, always with a sign. */
    points: (value: number) => `${value > 0 ? '+' : value < 0 ? '−' : ''}${numberFormat.format(Math.abs(value))}`,
  };
}

/** A titled box that fills its grid cell; its body takes whatever height is left and clips. */
export function Panel({ title, caption, children }: { title: string; caption?: string; children: ReactNode }) {
  return (
    <section className="flex h-full min-h-0 min-w-0 flex-col rounded border border-border bg-panel p-3.5 shadow-recessed">
      <h3 className="shrink-0 truncate font-display text-sm font-semibold uppercase tracking-[0.07em] text-ink">{title}</h3>
      {caption && (
        <p className="shrink-0 truncate text-xs text-ink-soft" title={caption}>
          {caption}
        </p>
      )}
      <div className="mt-2.5 min-h-0 flex-1">{children}</div>
    </section>
  );
}

// ------------------------------------------------------------------------------------ tiles

function Tile({ label, value, detail }: { label: string; value: ReactNode; detail: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col justify-center rounded border border-border bg-panel px-4 shadow-recessed">
      <div className="truncate font-display text-label font-semibold uppercase tracking-[0.07em] text-ink-soft">{label}</div>
      <div className="mt-0.5 truncate font-display text-3xl font-bold text-ink">{value}</div>
      <div className="truncate text-xs text-ink-soft">{detail}</div>
    </div>
  );
}

export function OverviewTiles({ data, selected }: { data: ExecutiveOverviewDto; selected: OverviewProjectDto | null }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();
  const tiles = data.tiles;
  const dash = t('common.empty');

  const planDelta =
    tiles.planMetLast14Days !== null && tiles.planMetPrevious14Days !== null
      ? Math.round(tiles.planMetLast14Days * 100) - Math.round(tiles.planMetPrevious14Days * 100)
      : null;

  return (
    <div className="grid h-full grid-cols-5 gap-3.5">
      {selected ? (
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
                : t('executiveOverview.tiles.againstTime', {
                    elapsed: format.percent(selected.timeElapsed),
                    points: format.points(selected.scheduleGapPoints),
                  })
            }
          />
        </>
      ) : (
        <>
          <Tile
            label={t('executiveOverview.tiles.projects')}
            value={format.number(tiles.projectCount)}
            detail={t('executiveOverview.tiles.projectsDetail', {
              active: tiles.activeCount,
              onHold: tiles.onHoldCount,
              completed: tiles.completedCount,
            })}
          />
          <Tile
            label={t('executiveOverview.tiles.workComplete')}
            value={format.percent(tiles.workComplete)}
            detail={t('executiveOverview.tiles.workCompleteDetail', { n: tiles.activeBehindCount })}
          />
        </>
      )}
      <Tile
        label={t('executiveOverview.tiles.planMet')}
        value={tiles.planMetLast14Days === null ? dash : format.percent(tiles.planMetLast14Days)}
        detail={
          tiles.planMetLast14Days === null ? (
            t('executiveOverview.tiles.noReports14')
          ) : planDelta === null ? (
            t('executiveOverview.tiles.reportCount', { n: tiles.reportsLast14Days })
          ) : (
            <>
              <span className={planDelta >= 0 ? 'text-success' : 'text-danger'}>
                {planDelta >= 0 ? '▲' : '▼'} {t('executiveOverview.points', { points: format.number(Math.abs(planDelta)) })}
              </span>{' '}
              {t('executiveOverview.tiles.againstPrevious14')}
            </>
          )
        }
      />
      <Tile
        label={t('executiveOverview.tiles.workers')}
        value={tiles.workersOnSite === null ? dash : format.number(tiles.workersOnSite)}
        detail={
          tiles.workersOnSiteDate === null
            ? t('executiveOverview.tiles.noReports')
            : t('executiveOverview.tiles.workersDetail', { date: format.day(tiles.workersOnSiteDate) })
        }
      />
      <Tile
        label={t('executiveOverview.tiles.delays')}
        value={tiles.delayedShareLast30Days === null ? dash : format.percent(tiles.delayedShareLast30Days)}
        detail={
          tiles.delayedShareLast30Days === null
            ? t('executiveOverview.tiles.noReports30')
            : t('executiveOverview.tiles.delaysDetail', { delayed: tiles.delayedReportsLast30Days, total: tiles.reportsLast30Days })
        }
      />
    </div>
  );
}

// -------------------------------------------------------------------------- project table

/** A filled bar with an optional marker - used for work complete (marker = time elapsed). */
function ProgressBar({ share, marker, title }: { share: number; marker?: number | null; title?: string }) {
  return (
    <span className="relative block h-2 min-w-[56px] flex-1 rounded bg-border" title={title}>
      <span className="absolute inset-y-0 start-0 rounded bg-accent" style={{ width: `${Math.round(share * 100)}%` }} />
      {marker !== null && marker !== undefined && (
        <span className="absolute -bottom-1 -top-1 w-0.5 bg-ink" style={{ insetInlineStart: `calc(${Math.round(marker * 100)}% - 1px)` }} />
      )}
    </span>
  );
}

const SCHEDULE_TONE = { ahead: 'success', behind: 'danger', on_track: 'muted', finished: 'accent', unknown: 'muted' } as const;
const STATUS_TONE: Record<string, 'success' | 'danger' | 'accent' | 'muted'> = {
  active: 'success',
  on_hold: 'danger',
  completed: 'accent',
  planning: 'muted',
  cancelled: 'muted',
};

/** A project that isn't finished and hasn't reported for longer than this is called out. */
const QUIET_AFTER_DAYS = 3;

/**
 * One row per reporting project. The panel holds about five rows; with more projects the
 * list scrolls inside the panel (its header stays put) rather than pushing the page taller.
 */
export function ProjectsTable({
  projects,
  selectedId,
  onSelect,
}: {
  projects: OverviewProjectDto[];
  selectedId: string | null;
  onSelect: (projectId: string | null) => void;
}) {
  const { t } = useTranslation();
  const format = useOverviewFormat();
  const dash = t('common.empty');

  if (projects.length === 0) return <p className="text-sm text-ink-soft">{t('executiveOverview.projects.empty')}</p>;

  return (
    <div className="h-full overflow-auto">
      <table className="w-full border-collapse text-sm">
        <thead>
          <tr>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.projects.project')}</th>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.projects.status')}</th>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.projects.nowIn')}</th>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.projects.workComplete')}</th>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.projects.phases')}</th>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.projects.schedule')}</th>
            <th className={`${TABLE_HEAD_CLASS} text-end`}>{t('executiveOverview.projects.planMet')}</th>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.projects.lastReport')}</th>
          </tr>
        </thead>
        <tbody>
          {projects.map((project) => {
            const isSelected = project.id === selectedId;
            const finished = project.scheduleStatus === 'finished';
            const quiet = !finished && project.daysSinceLastReport > QUIET_AFTER_DAYS;
            const toggle = () => onSelect(isSelected ? null : project.id);
            return (
              <tr
                key={project.id}
                tabIndex={0}
                aria-selected={isSelected}
                onClick={toggle}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    toggle();
                  }
                }}
                className={`cursor-pointer border-b border-border outline-none last:border-b-0 hover:bg-bg focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-accent ${
                  isSelected ? 'bg-bg' : ''
                }`}
              >
                <td className={`max-w-[236px] border-s-2 px-1.5 py-1 ${isSelected ? 'border-s-accent' : 'border-s-transparent'}`}>
                  <div className="truncate font-semibold leading-5 text-ink" dir="auto" title={project.name}>
                    {project.name}
                  </div>
                  <div className="truncate text-xs leading-4 text-ink-soft">
                    <span className="font-mono">{project.code}</span>
                    {project.city && (
                      <>
                        {' · '}
                        <span dir="auto">{project.city}</span>
                      </>
                    )}
                  </div>
                </td>
                <td className="whitespace-nowrap px-1.5 py-1">
                  {project.status ? (
                    <StatusLed
                      label={t(`executiveOverview.projectStatus.${project.status}`, project.status)}
                      tone={STATUS_TONE[project.status] ?? 'muted'}
                    />
                  ) : (
                    dash
                  )}
                </td>
                <td className="whitespace-nowrap px-1.5 py-1 text-ink">
                  {finished || project.currentPhases.length === 0
                    ? dash
                    : project.currentPhases.map((phase) => t(`executiveOverview.phase.${phase}`, phase)).join(' + ')}
                </td>
                <td className="px-1.5 py-1">
                  <div className="flex min-w-[112px] items-center gap-1.5">
                    <span className="w-10 shrink-0 font-mono text-xs text-ink">{format.percent(project.workComplete)}</span>
                    <ProgressBar
                      share={project.workComplete}
                      marker={project.timeElapsed}
                      title={
                        project.timeElapsed === null
                          ? undefined
                          : t('executiveOverview.projects.barTitle', {
                              work: format.percent(project.workComplete),
                              elapsed: format.percent(project.timeElapsed),
                            })
                      }
                    />
                  </div>
                </td>
                <td className="px-1.5 py-1">
                  <div className="flex gap-1">
                    {project.phases.map(({ phase, complete }) => (
                      <div key={phase} className="w-8" title={`${t(`executiveOverview.phase.${phase}`, phase)} ${format.percent(complete)}`}>
                        <span className="relative block h-2 overflow-hidden rounded bg-border">
                          <span className="absolute inset-y-0 start-0 bg-accent" style={{ width: `${Math.round(complete * 100)}%` }} />
                        </span>
                        <div className="whitespace-nowrap text-center text-micro leading-4 text-ink-soft">
                          {t(`executiveOverview.phaseShort.${phase}`, phase.charAt(0).toUpperCase())}
                        </div>
                      </div>
                    ))}
                  </div>
                </td>
                <td className="whitespace-nowrap px-1.5 py-1">
                  <StatusLed
                    tone={SCHEDULE_TONE[project.scheduleStatus] ?? 'muted'}
                    label={
                      finished
                        ? t('executiveOverview.schedule.finished', { date: format.day(project.lastReportDate) })
                        : project.scheduleStatus === 'ahead' || project.scheduleStatus === 'behind'
                          ? t(`executiveOverview.schedule.${project.scheduleStatus}`, {
                              points: format.points(project.scheduleGapPoints ?? 0),
                            })
                          : t(`executiveOverview.schedule.${project.scheduleStatus}`)
                    }
                  />
                </td>
                <td className="px-1.5 py-1 text-end font-mono text-xs text-ink">{format.percent(project.planMet)}</td>
                <td className="whitespace-nowrap px-1.5 py-1">
                  <div className={`font-mono text-xs ${quiet ? 'text-danger' : 'text-ink'}`}>{format.day(project.lastReportDate)}</div>
                  {quiet && (
                    <div className="text-micro text-danger">{t('executiveOverview.projects.quiet', { n: project.daysSinceLastReport })}</div>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

// ---------------------------------------------------------------------------------- stock

/** How many materials the stock panel has room for; the rest are counted in its caption. */
export const STOCK_ROWS_SHOWN = 8;

export function StockTable({ materials }: { materials: OverviewStockMaterialDto[] }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();

  if (materials.length === 0) return <p className="text-sm text-ink-soft">{t('executiveOverview.stock.empty')}</p>;

  return (
    <div className="h-full overflow-auto">
      <table className="w-full border-collapse text-sm">
        <thead>
          <tr>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.stock.material')}</th>
            <th className={`${TABLE_HEAD_CLASS} text-end`}>{t('executiveOverview.stock.issued')}</th>
            <th className={`${TABLE_HEAD_CLASS} text-end`}>{t('executiveOverview.stock.onHand')}</th>
            <th className={TABLE_HEAD_CLASS}>{t('executiveOverview.stock.issuedShare')}</th>
          </tr>
        </thead>
        <tbody>
          {materials.slice(0, STOCK_ROWS_SHOWN).map((material) => (
            <tr key={`${material.isCustomMaterial}-${material.material}`} className="border-b border-border last:border-b-0">
              <td className="max-w-[132px] truncate px-1.5 py-0.5">
                <span dir="auto" className="text-ink">
                  {/* A material typed into the "other" box has no translation - it is shown as written. */}
                  {material.isCustomMaterial ? material.material : t(`executiveOverview.material.${material.material}`, material.material)}
                </span>{' '}
                {material.unit && <span className="text-xs text-ink-soft">{t(`executiveOverview.unit.${material.unit}`, material.unit)}</span>}
              </td>
              <td className="px-1.5 py-0.5 text-end font-mono text-xs text-ink">{format.number(material.issued)}</td>
              <td className="px-1.5 py-0.5 text-end font-mono text-xs text-ink">{format.number(material.onHand)}</td>
              <td className="px-1.5 py-1">
                <div className="flex min-w-[84px] items-center gap-1.5">
                  <ProgressBar share={Math.min(1, material.issuedShare)} />
                  <span className="w-9 shrink-0 text-end font-mono text-xs text-ink">{format.percent(material.issuedShare)}</span>
                </div>
              </td>
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

export function ProblemsList({ problems }: { problems: ExecutiveOverviewDto['latestProblems'] }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();

  if (problems.length === 0) return <p className="text-sm text-ink-soft">{t('executiveOverview.problems.empty')}</p>;

  return (
    <ul className="flex h-full flex-col overflow-hidden">
      {problems.slice(0, PROBLEMS_SHOWN).map((problem, index) => (
        <li key={index} className="grid grid-cols-[4.75rem_1fr] gap-2.5 border-b border-border py-1.5 last:border-b-0">
          <div>
            <div className="font-mono text-xs text-ink">{format.day(problem.date)}</div>
            <div className="truncate text-micro text-ink-soft">{t(`executiveOverview.phase.${problem.phase}`, problem.phase)}</div>
          </div>
          <div className="min-w-0">
            <div dir="auto" className="line-clamp-2 text-sm leading-snug text-ink" title={problem.description}>
              {problem.description}
            </div>
            <div dir="auto" className="truncate text-xs text-ink-soft">
              {problem.projectName}
            </div>
          </div>
        </li>
      ))}
    </ul>
  );
}
