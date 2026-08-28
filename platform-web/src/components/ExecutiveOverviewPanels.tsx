import { useTranslation } from 'react-i18next';
import type { OverdueTaskDto, ProjectCrewCountDto, ProjectProgressDto, ProjectWeatherSummaryDto } from '../types';
import { StatusLed } from './StatusLed';

const PANEL_CLASS = 'rounded border border-border bg-panel p-4 shadow-recessed';
const PANEL_TITLE_CLASS = 'mb-3 font-display text-sm font-semibold uppercase tracking-[0.07em] text-ink';

function formatDate(iso: string, locale: string) {
  return new Date(iso).toLocaleDateString(locale, { year: 'numeric', month: 'short', day: 'numeric' });
}

export function ProjectProgressPanel({ projects }: { projects: ProjectProgressDto[] }) {
  const { t } = useTranslation();

  return (
    <div className={PANEL_CLASS}>
      <h3 className={PANEL_TITLE_CLASS}>{t('executiveOverview.projectProgress.title')}</h3>
      {projects.length === 0 ? (
        <p className="text-sm text-ink-soft">{t('executiveOverview.projectProgress.empty')}</p>
      ) : (
        <div className="space-y-3">
          {projects.map((project) => (
            <div key={project.projectId}>
              <div className="mb-1 flex items-center justify-between gap-2 text-sm">
                <span className="text-ink">
                  <span className="font-medium">{project.projectCode}</span>
                  <span className="ms-2 text-ink-soft">{project.projectName}</span>
                </span>
                {project.status && (
                  <StatusLed
                    label={t(`executiveOverview.projectStatus.${project.status}`, project.status)}
                    tone={project.status === 'completed' ? 'success' : project.status === 'on_hold' ? 'danger' : 'accent'}
                  />
                )}
              </div>
              {project.percentComplete === null ? (
                <p className="text-xs text-ink-soft">{t('common.empty')}</p>
              ) : (
                <div className="flex items-center gap-2">
                  <div className="h-2 flex-1 overflow-hidden rounded bg-border">
                    <div className="h-full bg-accent" style={{ width: `${project.percentComplete}%` }} />
                  </div>
                  <span className="w-10 shrink-0 text-end font-mono text-xs text-ink-soft">
                    {Math.round(project.percentComplete)}%
                  </span>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function CrewCountPanel({ crewCounts }: { crewCounts: ProjectCrewCountDto[] }) {
  const { t, i18n } = useTranslation();

  // One row per project - the most recent logged date, since that's what "current crew"
  // means for an overview. crewCounts already carries every (project, date) pair, so a
  // future dashboard wanting the full trend can consume the same data differently.
  const latestPerProject = Object.values(
    crewCounts.reduce<Record<string, ProjectCrewCountDto>>((acc, row) => {
      const existing = acc[row.projectId];
      if (!existing || row.logDate > existing.logDate) acc[row.projectId] = row;
      return acc;
    }, {}),
  ).sort((a, b) => a.projectCode.localeCompare(b.projectCode));

  return (
    <div className={PANEL_CLASS}>
      <h3 className={PANEL_TITLE_CLASS}>{t('executiveOverview.crewCount.title')}</h3>
      {latestPerProject.length === 0 ? (
        <p className="text-sm text-ink-soft">{t('executiveOverview.crewCount.empty')}</p>
      ) : (
        <div className="space-y-2">
          {latestPerProject.map((row) => (
            <div key={row.projectId} className="flex items-center justify-between text-sm">
              <span className="text-ink">
                <span className="font-medium">{row.projectCode}</span>
                <span className="ms-2 text-ink-soft">{row.projectName}</span>
              </span>
              <span className="text-end">
                <span className="font-mono text-base font-semibold text-ink">{row.totalHeadcount}</span>
                <span className="ms-2 text-xs text-ink-soft">
                  {t('executiveOverview.crewCount.asOf', { date: formatDate(row.logDate, i18n.language) })}
                </span>
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function WeatherImpactPanel({ weatherSummaries }: { weatherSummaries: ProjectWeatherSummaryDto[] }) {
  const { t } = useTranslation();

  return (
    <div className={PANEL_CLASS}>
      <h3 className={PANEL_TITLE_CLASS}>{t('executiveOverview.weatherImpact.title')}</h3>
      {weatherSummaries.length === 0 ? (
        <p className="text-sm text-ink-soft">{t('executiveOverview.weatherImpact.empty')}</p>
      ) : (
        <div className="space-y-3">
          {weatherSummaries.map((summary) => {
            const pct = summary.totalDays === 0 ? 0 : (summary.impactedDays / summary.totalDays) * 100;
            return (
              <div key={summary.projectId}>
                <div className="mb-1 flex items-center justify-between text-sm">
                  <span className="text-ink">
                    <span className="font-medium">{summary.projectCode}</span>
                    <span className="ms-2 text-ink-soft">{summary.projectName}</span>
                  </span>
                  <span className="text-xs text-ink-soft">
                    {t('executiveOverview.weatherImpact.daysImpacted', {
                      impacted: summary.impactedDays,
                      total: summary.totalDays,
                    })}
                  </span>
                </div>
                <div className="h-2 overflow-hidden rounded bg-border">
                  <div className="h-full bg-danger" style={{ width: `${pct}%` }} />
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

const PRIORITY_TONE: Record<string, 'muted' | 'accent' | 'danger'> = {
  low: 'muted',
  medium: 'accent',
  high: 'danger',
  urgent: 'danger',
};

export function OverdueTasksPanel({ tasks }: { tasks: OverdueTaskDto[] }) {
  const { t, i18n } = useTranslation();

  return (
    <div className={PANEL_CLASS}>
      <h3 className={PANEL_TITLE_CLASS}>{t('executiveOverview.overdueTasks.title', { count: tasks.length })}</h3>
      {tasks.length === 0 ? (
        <p className="text-sm text-ink-soft">{t('executiveOverview.overdueTasks.empty')}</p>
      ) : (
        <div className="max-h-80 space-y-2 overflow-y-auto">
          {tasks.map((task) => (
            <div key={task.taskId} className="border border-border rounded bg-bg p-2.5">
              <div className="flex items-center justify-between gap-2">
                <span className="font-mono text-xs text-ink-soft">{task.taskReference}</span>
                <StatusLed label={t(`executiveOverview.priority.${task.priority}`, task.priority)} tone={PRIORITY_TONE[task.priority] ?? 'muted'} />
              </div>
              <p className="mt-1 text-sm text-ink">{task.description}</p>
              <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-ink-soft">
                <span>{task.projectCode || task.projectName}</span>
                <span>{t('executiveOverview.overdueTasks.assignedTo', { name: task.assignedTo })}</span>
                <span>{t('executiveOverview.overdueTasks.dueDate', { date: formatDate(task.dueDateUtc, i18n.language) })}</span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
