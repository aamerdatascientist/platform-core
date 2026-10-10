import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { BarList, WeekColumnChart, WeekLineChart } from '../components/ExecutiveOverviewCharts';
import {
  OverviewTiles,
  Panel,
  ProblemsList,
  ProjectsTable,
  StockTable,
  useOverviewFormat,
} from '../components/ExecutiveOverviewPanels';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { ExecutiveOverviewDto } from '../types';

/** A "nice" y-axis for a count: a round step and the first multiple of it at or above the data. */
function countAxis(maxValue: number) {
  const step = maxValue > 400 ? 200 : maxValue > 200 ? 100 : maxValue > 80 ? 40 : maxValue > 40 ? 20 : maxValue > 16 ? 10 : 5;
  const top = Math.max(step, Math.ceil(maxValue / step) * step);
  const ticks: number[] = [];
  for (let tick = 0; tick <= top; tick += step) ticks.push(tick);
  return { top, ticks };
}

/**
 * One page, one request: everything shown comes from GET /api/analytics/executive-overview,
 * which reads the seven live forms and does all the arithmetic server-side (see
 * ExecutiveOverviewCalculator). Picking a project - from the dropdown or by clicking its
 * row - re-asks the server for that project's figures; the project table itself always
 * lists every reporting project.
 */
export function ExecutiveOverview({ token }: { token: string }) {
  const { t } = useTranslation();
  const format = useOverviewFormat();
  const [data, setData] = useState<ExecutiveOverviewDto | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useErrorMessage();
  // Answers can come back out of order when the selection changes quickly - only the
  // latest request is allowed to update the page.
  const latestRequest = useRef(0);

  useEffect(() => {
    const requestId = ++latestRequest.current;
    setLoading(true);
    setError(null);
    api.analytics
      .executiveOverview(token, selectedId)
      .then((result) => {
        if (requestId === latestRequest.current) setData(result);
      })
      .catch((err) => {
        if (requestId === latestRequest.current) setError({ err, fallbackKey: 'executiveOverview.loadError' });
      })
      .finally(() => {
        if (requestId === latestRequest.current) setLoading(false);
      });
  }, [token, selectedId]);

  if (!data) {
    if (error) return <p className="text-sm text-danger">{error}</p>;
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
      </div>
    );
  }

  const selected = data.projects.find((project) => project.id === selectedId) ?? null;
  const reportsSince = data.dataFrom ? format.day(data.dataFrom) : '';

  const planPoints = data.planMetByWeek.map((week) => ({
    label: format.day(week.weekStart),
    value: week.planMet === null ? null : week.planMet * 100,
    tooltip: [
      t('executiveOverview.weekOf', { date: format.day(week.weekStart) }),
      week.planMet === null
        ? t('executiveOverview.noReportsThisWeek')
        : t('executiveOverview.planMetWeek.tooltip', { share: format.percent(week.planMet), n: week.reportCount }),
    ],
  }));
  const lastPlanWeek = [...data.planMetByWeek].reverse().find((week) => week.planMet !== null);

  const workersAxis = countAxis(Math.max(0, ...data.workersByWeek.map((week) => week.averageWorkersPerDay)));
  const workerPoints = data.workersByWeek.map((week) => ({
    label: format.day(week.weekStart),
    value: week.averageWorkersPerDay,
    tooltip: [
      t('executiveOverview.weekOf', { date: format.day(week.weekStart) }),
      week.reportingDays === 0
        ? t('executiveOverview.noReportsThisWeek')
        : t('executiveOverview.workersWeek.tooltip', {
            workers: format.number(Math.round(week.averageWorkersPerDay)),
            n: week.reportingDays,
          }),
    ],
  }));

  const weatherItems = data.planMetByWeather.map((row) => ({
    label: t(`executiveOverview.weather.${row.weather}`, row.weather),
    value: row.planMet,
    text: format.percent(row.planMet),
    title: t('executiveOverview.planMetWeather.tooltip', { share: format.percent(row.planMet), n: row.reportCount }),
  }));

  const delayItems = data.delayCauses.map((row) => ({
    label: row.label,
    value: row.count,
    text: format.number(row.count),
    title: t('executiveOverview.delayCauses.tooltip', { n: row.count }),
  }));

  const wasted = data.issueReasons.find((row) => row.label === 'damaged_or_wasted')?.count ?? 0;
  const reasonItems = data.issueReasons.map((row) => ({
    label: t(`executiveOverview.reason.${row.label}`, row.label),
    value: row.count,
    text: format.number(row.count),
    title: t('executiveOverview.issueReasons.tooltip', {
      n: row.count,
      total: data.stockIssueCount,
      share: format.percent(data.stockIssueCount ? row.count / data.stockIssueCount : 0),
    }),
    tone: row.label === 'damaged_or_wasted' ? ('danger' as const) : ('accent' as const),
  }));

  return (
    <div className={`max-w-6xl space-y-4 ${loading ? 'opacity-70' : ''}`} aria-busy={loading}>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{t('executiveOverview.title')}</h2>
          <p className="text-sm font-normal text-ink-soft">
            {data.dataFrom
              ? t('executiveOverview.subtitle', { asOf: format.day(data.asOf), from: reportsSince })
              : t('executiveOverview.subtitleEmpty')}
          </p>
        </div>
        <label className="flex items-center gap-2 text-xs font-normal text-ink-soft">
          {t('executiveOverview.filter.label')}
          <select
            id="executive-overview-project"
            className="max-w-[16rem] rounded border border-border bg-panel px-2 py-1.5 text-sm text-ink"
            value={selectedId ?? ''}
            onChange={(event) => setSelectedId(event.target.value || null)}
          >
            <option value="">{t('executiveOverview.filter.all')}</option>
            {data.projects.map((project) => (
              <option key={project.id} value={project.id}>
                {project.code} · {project.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      <OverviewTiles data={data} selected={selected} />

      <Panel title={t('executiveOverview.projects.title')} caption={t('executiveOverview.projects.caption')}>
        <ProjectsTable projects={data.projects} selectedId={selectedId} onSelect={setSelectedId} />
      </Panel>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Panel title={t('executiveOverview.planMetWeek.title')} caption={t('executiveOverview.planMetWeek.caption')}>
          <WeekLineChart
            points={planPoints}
            max={100}
            ticks={[0, 25, 50, 75, 100]}
            formatTick={(value) => format.percent(value / 100)}
            endLabel={lastPlanWeek?.planMet != null ? format.percent(lastPlanWeek.planMet) : undefined}
            ariaLabel={t('executiveOverview.planMetWeek.title')}
            emptyText={t('executiveOverview.noReports')}
          />
        </Panel>
        <Panel
          title={t('executiveOverview.workersWeek.title')}
          caption={t(selected ? 'executiveOverview.workersWeek.captionOne' : 'executiveOverview.workersWeek.captionAll')}
        >
          <WeekColumnChart
            points={workerPoints}
            max={workersAxis.top}
            ticks={workersAxis.ticks}
            formatTick={(value) => format.number(value)}
            ariaLabel={t('executiveOverview.workersWeek.title')}
            emptyText={t('executiveOverview.noReports')}
          />
        </Panel>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Panel title={t('executiveOverview.planMetWeather.title')} caption={t('executiveOverview.planMetWeather.caption')}>
          <BarList items={weatherItems} max={1} emptyText={t('executiveOverview.planMetWeather.empty')} />
        </Panel>
        <Panel
          title={t('executiveOverview.delayCauses.title')}
          caption={t('executiveOverview.delayCauses.caption', { n: data.delayedReportCount, from: reportsSince })}
        >
          <BarList
            items={delayItems}
            max={Math.max(1, ...delayItems.map((item) => item.value))}
            emptyText={t('executiveOverview.delayCauses.empty')}
          />
        </Panel>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Panel title={t('executiveOverview.stock.title')} caption={t('executiveOverview.stock.caption')}>
          <StockTable materials={data.stockByMaterial} />
        </Panel>
        <Panel
          title={t('executiveOverview.issueReasons.title')}
          caption={
            data.stockIssueCount
              ? t('executiveOverview.issueReasons.caption', {
                  n: data.stockIssueCount,
                  wasted,
                  share: format.percent(wasted / data.stockIssueCount),
                })
              : undefined
          }
        >
          <BarList
            items={reasonItems}
            max={Math.max(1, ...reasonItems.map((item) => item.value))}
            emptyText={t('executiveOverview.issueReasons.empty')}
          />
        </Panel>
      </div>

      <Panel
        title={t('executiveOverview.problems.title')}
        caption={t('executiveOverview.problems.caption', { n: data.problemReportCount, from: reportsSince })}
      >
        <ProblemsList problems={data.latestProblems} />
      </Panel>

      <div className="max-w-3xl space-y-1.5 text-xs font-normal text-ink-soft">
        <p>{t('executiveOverview.notes.method')}</p>
        {data.projectsWithoutReports > 0 && (
          <p>{t('executiveOverview.notes.withoutReports', { n: data.projectsWithoutReports })}</p>
        )}
      </div>
    </div>
  );
}
