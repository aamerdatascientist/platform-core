import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError, api } from '../api/client';
import { BarList, WeekColumnChart, WeekLineChart } from '../components/ExecutiveOverviewCharts';
import {
  OverviewTiles,
  PROBLEMS_SHOWN,
  Panel,
  ProblemsList,
  ProjectsTable,
  STOCK_ROWS_SHOWN,
  StockTable,
  useOverviewFormat,
} from '../components/ExecutiveOverviewPanels';
import { LanguageToggle } from '../components/LanguageToggle';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { Logo } from '../components/Logo';
import { ModeToggle } from '../components/ModeToggle';
import { buildDashboardUrl, readDashboardLink } from '../dashboardLink';
import { getMode, onModeChanged, setMode } from '../theme/mode';
import type { ExecutiveOverviewDto } from '../types';

/**
 * The page is drawn once, on a fixed 16:9 "stage" of this size, and the whole stage is then
 * scaled to fit whatever window it is in. That is what makes it exactly 16:9 everywhere -
 * a wall screen, a laptop, a projector - with every panel keeping the same place and
 * proportions, instead of reflowing differently on each screen.
 */
const STAGE_WIDTH = 1760;
const STAGE_HEIGHT = 990;

/** How often an open dashboard re-reads its data, so a screen left on stays current. */
const REFRESH_MS = 5 * 60 * 1000;

const DELAY_CAUSES_SHOWN = 4;

function useStageScale() {
  const [scale, setScale] = useState(1);
  useEffect(() => {
    const fit = () => setScale(Math.min(window.innerWidth / STAGE_WIDTH, window.innerHeight / STAGE_HEIGHT));
    fit();
    window.addEventListener('resize', fit);
    return () => window.removeEventListener('resize', fit);
  }, []);
  return scale;
}

/** A "nice" y-axis for a count: a round step and the first multiple of it at or above the data. */
function countAxis(maxValue: number) {
  const step = maxValue > 400 ? 200 : maxValue > 200 ? 100 : maxValue > 80 ? 40 : maxValue > 40 ? 20 : maxValue > 16 ? 10 : 5;
  const top = Math.max(step, Math.ceil(maxValue / step) * step);
  const ticks: number[] = [];
  for (let tick = 0; tick <= top; tick += step) ticks.push(tick);
  return { top, ticks };
}

function Stage({ children }: { children: ReactNode }) {
  const scale = useStageScale();
  return (
    <div className="fixed inset-0 overflow-hidden bg-bg">
      <div
        className="absolute left-1/2 top-1/2"
        style={{ width: STAGE_WIDTH, height: STAGE_HEIGHT, transform: `translate(-50%, -50%) scale(${scale})` }}
      >
        {children}
      </div>
    </div>
  );
}

function Message({ title, body }: { title: string; body?: string }) {
  return (
    <Stage>
      <div className="flex h-full flex-col items-center justify-center gap-3 text-center">
        <Logo size="lg" />
        <h1 className="font-display text-2xl font-semibold text-ink">{title}</h1>
        {body && <p className="max-w-xl text-base text-ink-soft">{body}</p>}
      </div>
    </Stage>
  );
}

const HEADER_BUTTON_CLASS =
  'rounded border border-border bg-panel px-3 py-1.5 text-xs font-medium text-ink-soft hover:text-ink focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent';

/**
 * The Executive Overview as a standalone, full-window page.
 *
 * It works two ways. Opened from a link that carries a share key (#k=...), it needs no
 * sign-in and reads the public endpoint - this is the link the app itself opens, and the
 * one that can be passed on. Opened without a key, it falls back to the signed-in session
 * (only possible in a tab the app opened, which inherits that session).
 */
export function ExecutiveOverview({ token }: { token: string | null }) {
  const { t, i18n } = useTranslation();
  const format = useOverviewFormat();
  const [link] = useState(() => readDashboardLink(window.location.hash));
  const [data, setData] = useState<ExecutiveOverviewDto | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [failure, setFailure] = useState<'invalid' | 'error' | null>(null);
  const [refreshTick, setRefreshTick] = useState(0);
  const [mode, setCurrentMode] = useState(getMode());
  const [copied, setCopied] = useState(false);
  const [isFullScreen, setIsFullScreen] = useState(false);
  // Answers can come back out of order when the selection changes quickly - only the
  // latest request is allowed to update the page.
  const latestRequest = useRef(0);

  // A shared link opens in the language and colour mode it was sent with.
  useEffect(() => {
    if (link.language && link.language !== i18n.language) i18n.changeLanguage(link.language);
    if (link.mode) setMode(link.mode as 'dark' | 'light');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => onModeChanged(setCurrentMode), []);

  useEffect(() => {
    document.title = `${t('executiveOverview.title')} · Asas`;
    return () => {
      document.title = 'Asas';
    };
  }, [t, i18n.language]);

  useEffect(() => {
    const timer = window.setInterval(() => setRefreshTick((tick) => tick + 1), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const onChange = () => setIsFullScreen(document.fullscreenElement !== null);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  useEffect(() => {
    if (!link.shareKey && !token) return;
    const requestId = ++latestRequest.current;
    const load = link.shareKey
      ? api.analytics.publicExecutiveOverview(link.shareKey, selectedId)
      : api.analytics.executiveOverview(token!, selectedId);
    load
      .then((result) => {
        if (requestId !== latestRequest.current) return;
        setData(result);
        setFailure(null);
      })
      .catch((err) => {
        if (requestId !== latestRequest.current) return;
        // The public endpoint answers 404 for a missing, wrong or retired key.
        setFailure(link.shareKey && err instanceof ApiError && err.status === 404 ? 'invalid' : 'error');
      });
  }, [link.shareKey, token, selectedId, refreshTick]);

  if (!link.shareKey && !token) {
    return <Message title={t('executiveOverview.share.needLinkTitle')} body={t('executiveOverview.share.needLinkBody')} />;
  }
  if (failure === 'invalid') {
    return <Message title={t('executiveOverview.share.invalidTitle')} body={t('executiveOverview.share.invalidBody')} />;
  }
  if (!data) {
    if (failure === 'error') return <Message title={t('executiveOverview.loadError')} />;
    return (
      <Stage>
        <div className="flex h-full items-center justify-center gap-3">
          <LoadingSpinner size="md" />
          <span className="text-sm uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
        </div>
      </Stage>
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

  const delayItems = data.delayCauses.slice(0, DELAY_CAUSES_SHOWN).map((row) => ({
    label: row.label,
    value: row.count,
    text: format.number(row.count),
    title: t('executiveOverview.delayCauses.tooltip', { n: row.count }),
  }));

  const wasted = data.issueReasons.find((row) => row.label === 'damaged_or_wasted')?.count ?? 0;
  const hiddenMaterials = Math.max(0, data.stockByMaterial.length - STOCK_ROWS_SHOWN);
  const stockCaption = [
    data.stockIssueCount
      ? t('executiveOverview.stock.caption', {
          n: data.stockIssueCount,
          wasted,
          share: format.percent(wasted / data.stockIssueCount),
        })
      : null,
    hiddenMaterials ? t('executiveOverview.stock.more', { n: hiddenMaterials }) : null,
  ]
    .filter(Boolean)
    .join(' · ');

  const projectsCaption = [
    t('executiveOverview.projects.caption'),
    data.projectsWithoutReports > 0 ? t('executiveOverview.projects.withoutReports', { n: data.projectsWithoutReports }) : null,
  ]
    .filter(Boolean)
    .join(' · ');

  async function copyLink() {
    // The address in the bar may predate a language or mode change - rebuild it as shown now.
    const url = buildDashboardUrl({ shareKey: link.shareKey, language: i18n.language, mode });
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      window.prompt(t('executiveOverview.share.copyLink'), url);
    }
  }

  function toggleFullScreen() {
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    else document.documentElement.requestFullscreen().catch(() => {});
  }

  return (
    <Stage>
      <div className="grid h-full gap-3.5 p-6" style={{ gridTemplateRows: '52px 108px 356px minmax(0, 1fr)' }}>
        <header className="flex items-center justify-between gap-6">
          <div className="flex min-w-0 items-center gap-3">
            <Logo size="sm" />
            <div className="min-w-0">
              <h1 className="truncate font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">
                {t('executiveOverview.title')}
              </h1>
              <p className="truncate text-xs text-ink-soft">
                {data.dataFrom
                  ? t('executiveOverview.subtitle', { asOf: format.day(data.asOf), from: reportsSince })
                  : t('executiveOverview.subtitleEmpty')}
              </p>
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-3">
            <label className="flex items-center gap-2 text-xs text-ink-soft">
              {t('executiveOverview.filter.label')}
              <select
                id="executive-overview-project"
                className="w-72 rounded border border-border bg-panel px-2 py-1.5 text-sm text-ink"
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
            {link.shareKey && (
              <button type="button" className={HEADER_BUTTON_CLASS} onClick={copyLink}>
                {copied ? t('executiveOverview.share.copied') : t('executiveOverview.share.copyLink')}
              </button>
            )}
            <button type="button" className={HEADER_BUTTON_CLASS} onClick={toggleFullScreen}>
              {isFullScreen ? t('executiveOverview.share.exitFullScreen') : t('executiveOverview.share.fullScreen')}
            </button>
            <div dir="ltr" className="flex items-center gap-3">
              <LanguageToggle />
              <ModeToggle />
            </div>
          </div>
        </header>

        <OverviewTiles data={data} selected={selected} />

        <div className="grid min-h-0 grid-cols-12 gap-3.5">
          <div className="col-span-8 min-h-0">
            <Panel title={t('executiveOverview.projects.title')} caption={projectsCaption}>
              <ProjectsTable projects={data.projects} selectedId={selectedId} onSelect={setSelectedId} />
            </Panel>
          </div>
          <div className="col-span-4 min-h-0">
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
          </div>
        </div>

        <div className="grid min-h-0 grid-cols-4 gap-3.5">
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

          <div className="grid min-h-0 grid-rows-2 gap-3.5">
            <Panel title={t('executiveOverview.planMetWeather.title')}>
              <BarList items={weatherItems} max={1} emptyText={t('executiveOverview.planMetWeather.empty')} />
            </Panel>
            <Panel title={t('executiveOverview.delayCauses.title', { n: data.delayedReportCount })}>
              <BarList
                items={delayItems}
                max={Math.max(1, ...delayItems.map((item) => item.value))}
                emptyText={t('executiveOverview.delayCauses.empty')}
              />
            </Panel>
          </div>

          <Panel title={t('executiveOverview.stock.title')} caption={stockCaption || undefined}>
            <StockTable materials={data.stockByMaterial} />
          </Panel>

          <Panel
            title={t('executiveOverview.problems.title')}
            caption={t('executiveOverview.problems.caption', {
              n: data.problemReportCount,
              from: reportsSince,
              shown: Math.min(PROBLEMS_SHOWN, data.latestProblems.length),
            })}
          >
            <ProblemsList problems={data.latestProblems} />
          </Panel>
        </div>
      </div>
    </Stage>
  );
}
