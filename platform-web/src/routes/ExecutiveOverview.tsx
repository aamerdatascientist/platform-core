import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import {
  CrewCountPanel,
  OverdueTasksPanel,
  ProjectProgressPanel,
  WeatherImpactPanel,
} from '../components/ExecutiveOverviewPanels';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { ProjectSelector } from '../components/ProjectSelector';
import { StockMovementTree } from '../components/StockMovementTree';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type {
  OverdueTaskDto,
  ProjectCrewCountDto,
  ProjectProgressDto,
  ProjectWeatherSummaryDto,
  StockMovementBreakdownDto,
} from '../types';

interface DashboardData {
  progress: ProjectProgressDto[];
  crewCounts: ProjectCrewCountDto[];
  weather: ProjectWeatherSummaryDto[];
  overdueTasks: OverdueTaskDto[];
  stockMovements: StockMovementBreakdownDto[];
}

export function ExecutiveOverview({ token }: { token: string }) {
  const { t } = useTranslation();
  const [data, setData] = useState<DashboardData | null>(null);
  const [error, setError] = useErrorMessage();
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);

  useEffect(() => {
    load();
  }, [token]);

  async function load() {
    setError(null);
    try {
      const [progress, crewCounts, weather, overdueTasks, stockMovements] = await Promise.all([
        api.analytics.projectProgress(token),
        api.analytics.crewCountByProject(token),
        api.analytics.weatherImpactedDays(token),
        api.analytics.overdueTasks(token),
        api.analytics.stockMovementBreakdown(token),
      ]);
      setData({ progress, crewCounts, weather, overdueTasks, stockMovements });
    } catch (err) {
      setError({ err, fallbackKey: 'executiveOverview.loadError' });
    }
  }

  if (error) {
    return <p className="text-sm text-danger">{error}</p>;
  }

  if (!data) {
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
      </div>
    );
  }

  // The stock movement tree is deliberately NOT filtered by project - none of the four
  // movement forms has a real relationship to Projects (see the Phase 0 audit), so there's
  // nothing honest to filter it by.
  const filteredProgress = selectedProjectId ? data.progress.filter((p) => p.projectId === selectedProjectId) : data.progress;
  const filteredCrewCounts = selectedProjectId ? data.crewCounts.filter((c) => c.projectId === selectedProjectId) : data.crewCounts;
  const filteredWeather = selectedProjectId ? data.weather.filter((w) => w.projectId === selectedProjectId) : data.weather;
  const filteredOverdueTasks = selectedProjectId ? data.overdueTasks.filter((o) => o.projectId === selectedProjectId) : data.overdueTasks;

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">
            {t('executiveOverview.title')}
          </h2>
          <p className="text-sm text-ink-soft">{t('executiveOverview.subtitle')}</p>
        </div>
        <ProjectSelector token={token} value={selectedProjectId} onChange={setSelectedProjectId} />
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <ProjectProgressPanel projects={filteredProgress} />
        <CrewCountPanel crewCounts={filteredCrewCounts} />
        <WeatherImpactPanel weatherSummaries={filteredWeather} />
        <OverdueTasksPanel tasks={filteredOverdueTasks} />
      </div>

      <div className="rounded border border-border bg-panel p-4 shadow-recessed">
        <h3 className="mb-3 font-display text-sm font-semibold uppercase tracking-[0.07em] text-ink">
          {t('executiveOverview.stockMovement.title')}
        </h3>
        <StockMovementTree movements={data.stockMovements} />
      </div>
    </div>
  );
}
