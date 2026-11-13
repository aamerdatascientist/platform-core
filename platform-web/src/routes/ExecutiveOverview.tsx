import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError, api } from '../api/client';
import {
  HBars,
  OverviewTiles,
  PROBLEMS_SHOWN,
  Panel,
  planColour,
  ProblemsList,
  ProjectsTable,
  STOCK_ROWS_SHOWN,
  StockTable,
  useOverviewFormat,
  WeekBars,
  stageNameOf,
} from '../components/ExecutiveOverviewPanels';
import { Icon, LogoMark } from '../components/icons';
import { LanguageToggle } from '../components/LanguageToggle';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { Logo } from '../components/Logo';
import { ModeToggle } from '../components/ModeToggle';
import { buildDashboardUrl, readDashboardLink } from '../dashboardLink';
import { getMode, onModeChanged, setMode } from '../theme/mode';
import type { ExecutiveOverviewDto, OverviewPhase } from '../types';
import { pickLanguage } from '../forms/localized';

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

function Message({ title, body, onRetry }: { title: string; body?: string; onRetry?: () => void }) {
  const { t } = useTranslation();
  return (
    <Stage>
      <div className="flex h-full flex-col items-center justify-center gap-3 text-center">
        <Logo size="lg" />
        <h1 className="font-display text-2xl font-semibold text-ink">{title}</h1>
        {body && <p className="max-w-xl text-base text-ink-soft">{body}</p>}
        {onRetry && (
          <button type="button" onClick={onRetry} className={HEADER_BUTTON_CLASS}>
            {t('common.tryAgain')}
          </button>
        )}
      </div>
    </Stage>
  );
}

/** Header buttons (25): 42px, radius 11, --panel on 1px --border, 15px (the 1600x900 design x1.1). */
const HEADER_BUTTON_CLASS = 'relative flex min-h-[42px] min-w-0 items-center gap-2 rounded-[11px] py-1 text-start leading-tight border border-border bg-panel px-[15px] text-[15.4px] text-ink hover:border-ink-soft';

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
  const [selectedPhase, setSelectedPhase] = useState<OverviewPhase | null>(null);
  const [failure, setFailure] = useState<'invalid' | 'forbidden' | 'error' | null>(null);
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
      ? api.analytics.publicExecutiveOverview(link.shareKey, selectedId, selectedPhase)
      : api.analytics.executiveOverview(token!, selectedId, selectedPhase);
    load
      .then((result) => {
        if (requestId !== latestRequest.current) return;
        setData(result);
        setFailure(null);
      })
      .catch((err) => {
        if (requestId !== latestRequest.current) return;
        // The public endpoint answers 404 for a missing, wrong or retired key.
        if (link.shareKey && err instanceof ApiError && err.status === 404) setFailure('invalid');
        // Signed in, but not an administrator - the dashboard is for management only.
        else if (!link.shareKey && err instanceof ApiError && err.status === 403) setFailure('forbidden');
        else setFailure('error');
      });
  }, [link.shareKey, token, selectedId, selectedPhase, refreshTick]);

  if (!link.shareKey && !token) {
    return <Message title={t('executiveOverview.share.needLinkTitle')} body={t('executiveOverview.share.needLinkBody')} />;
  }
  if (failure === 'forbidden') {
    return <Message title={t('executiveOverview.share.forbiddenTitle')} body={t('executiveOverview.share.forbiddenBody')} />;
  }
  if (failure === 'invalid') {
    return <Message title={t('executiveOverview.share.invalidTitle')} body={t('executiveOverview.share.invalidBody')} />;
  }
  if (!data) {
    if (failure === 'error') return <Message title={t('executiveOverview.loadError')} onRetry={() => (setFailure(null), setRefreshTick((n) => n + 1))} />;
    return (
      <Stage>
        <div className="flex h-full items-center justify-center gap-3">
          <LoadingSpinner size="md" />
          <span className="text-sm text-ink-soft">{t('common.loading')}</span>
        </div>
      </Stage>
    );
  }

  const selected = data.projects.find((project) => project.id === selectedId) ?? null;
  const reportsSince = data.dataFrom ? format.day(data.dataFrom) : '';

  const planWeeks = data.planMetByWeek.map((week) => ({
    weekStart: week.weekStart,
    value: week.planMet === null ? null : week.planMet * 100,
    tooltip: [
      t('executiveOverview.weekOf', { date: format.day(week.weekStart) }),
      week.planMet === null ? t('executiveOverview.noReportsThisWeek') : t('executiveOverview.planMetWeek.tooltip', { share: format.percent(week.planMet), n: week.reportCount }),
    ].join(' · '),
  }));
  const workerWeeks = data.workersByWeek.map((week) => ({
    weekStart: week.weekStart,
    value: week.reportingDays === 0 ? null : week.averageWorkersPerDay,
    tooltip: [
      t('executiveOverview.weekOf', { date: format.day(week.weekStart) }),
      week.reportingDays === 0
        ? t('executiveOverview.noReportsThisWeek')
        : t('executiveOverview.workersWeek.tooltip', { workers: format.number(Math.round(week.averageWorkersPerDay)), n: week.reportingDays }),
    ].join(' · '),
  }));
  const workersMax = Math.max(1, ...data.workersByWeek.map((week) => week.averageWorkersPerDay));

  // Plan met by weather: bars in --accent, extreme heat in --accent2 (25).
  const weatherItems = data.planMetByWeather.map((row) => ({
    label: t(`executiveOverview.weather.${row.weather}`, row.weather),
    value: row.planMet,
    text: format.percent(row.planMet),
    title: t('executiveOverview.planMetWeather.tooltip', { share: format.percent(row.planMet), n: row.reportCount }),
    colour: row.weather === 'extreme_heat' ? 'var(--accent2)' : 'var(--accent)',
  }));
  // Why days were delayed: --danger bars; the causes are typed in Arabic, so they stay right-to-left (25).
  const delayItems = data.delayCauses.slice(0, DELAY_CAUSES_SHOWN).map((row) => ({
    label: row.label,
    value: row.count,
    text: format.number(row.count),
    title: t('executiveOverview.delayCauses.tooltip', { n: row.count }),
    colour: 'var(--danger)',
    rtl: /[\u0600-\u06FF]/.test(row.label),
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

  const filterButton = (label: string, value: string, select: ReactNode) => (
    <label className={HEADER_BUTTON_CLASS}>
      {label}: {value}
      <Icon name="chevronDown" size={14} />
      {select}
    </label>
  );
  const selectOverlay = 'absolute inset-0 cursor-pointer opacity-0';

  return (
    <Stage>
      {/* 25 at x1.1: 24/29px padding, 15px gaps; rows: header, tiles (119), projects + plan (330), the rest. */}
      <div className="grid h-full gap-[15px] px-[29px] py-[24px]" style={{ gridTemplateRows: 'auto auto 330px minmax(0, 1fr)' }}>
        <header className="flex items-center gap-[15px]">
          <LogoMark size={37} />
          <div className="flex min-w-0 flex-col">
            <h1 className="break-words text-[26.4px] font-extrabold leading-tight text-ink">{t('executiveOverview.title')}</h1>
            <span className="break-words text-[14.3px] leading-tight text-ink-soft" dir="auto">
              {data.dataFrom ? t('executiveOverview.subtitle', { asOf: format.day(data.asOf), from: reportsSince }) : t('executiveOverview.subtitleEmpty')}
            </span>
          </div>
          <span className="flex-1" />
          {filterButton(
            t('executiveOverview.filter.label'),
            selected ? `${selected.code} · ${selected.name}` : t('executiveOverview.filter.all'),
            <select id="executive-overview-project" aria-label={t('executiveOverview.filter.label')} className={selectOverlay} value={selectedId ?? ''} onChange={(event) => setSelectedId(event.target.value || null)}>
              <option value="">{t('executiveOverview.filter.all')}</option>
              {data.projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} · {project.name}
                </option>
              ))}
            </select>,
          )}
          {filterButton(
            t('executiveOverview.filter.stageLabel'),
            selectedPhase && data ? stageNameOf(data, selectedPhase, i18n.language, t) : t('executiveOverview.filter.stageAll'),
            <select id="executive-overview-stage" aria-label={t('executiveOverview.filter.stageLabel')} className={selectOverlay} value={selectedPhase ?? ''} onChange={(event) => setSelectedPhase((event.target.value || null) as OverviewPhase | null)}>
              <option value="">{t('executiveOverview.filter.stageAll')}</option>
              {/* The stages of the plans in scope, in plan order (2026-11-10). */}
              {(data?.stages ?? []).map((stage) => (
                <option key={stage.code} value={stage.code}>
                  {pickLanguage(stage.nameAr, stage.nameEn, i18n.language)}
                </option>
              ))}
            </select>,
          )}
          {link.shareKey && (
            <button type="button" className={HEADER_BUTTON_CLASS} onClick={copyLink}>
              {copied ? t('executiveOverview.share.copied') : t('executiveOverview.share.copyLink')}
            </button>
          )}
          <button
            type="button"
            className={`${HEADER_BUTTON_CLASS} w-[42px] shrink-0 justify-center !px-0`}
            onClick={toggleFullScreen}
            aria-label={isFullScreen ? t('executiveOverview.share.exitFullScreen') : t('executiveOverview.share.fullScreen')}
            title={isFullScreen ? t('executiveOverview.share.exitFullScreen') : t('executiveOverview.share.fullScreen')}
          >
            <Icon name="expand" size={18} />
          </button>
          {/* Kept from before: a shared link opens in its language and mode, and these switch them. */}
          <div dir="ltr" className="flex shrink-0 items-center gap-2">
            <LanguageToggle />
            <ModeToggle />
          </div>
        </header>

        <OverviewTiles data={data} selected={selected} phase={selectedPhase} />

        <div className="grid min-h-0 gap-[13px]" style={{ gridTemplateColumns: '1.55fr 1fr' }}>
          <Panel title={t('executiveOverview.projects.title')} aside={projectsCaption}>
            <ProjectsTable projects={data.projects} selectedId={selectedId} onSelect={setSelectedId} />
          </Panel>
          <Panel title={t('executiveOverview.planMetWeek.title')} aside={t('executiveOverview.planMetWeek.caption')}>
            <WeekBars weeks={planWeeks} max={100} colour={planColour} ariaLabel={t('executiveOverview.planMetWeek.title')} emptyText={t('executiveOverview.noReports')} />
          </Panel>
        </div>

        <div className="grid min-h-0 gap-[13px]" style={{ gridTemplateColumns: '1.3fr 0.8fr 1.2fr 0.9fr 0.9fr' }}>
          <Panel title={t('executiveOverview.workersWeek.title')} caption={t(selected ? 'executiveOverview.workersWeek.captionOne' : 'executiveOverview.workersWeek.captionAll')}>
            <WeekBars weeks={workerWeeks} max={workersMax} colour={() => 'var(--accent)'} ariaLabel={t('executiveOverview.workersWeek.title')} emptyText={t('executiveOverview.noReports')} labelEvery="thirds" />
          </Panel>
          <Panel title={t('executiveOverview.planMetWeather.title')}>
            <HBars items={weatherItems} max={1} thick emptyText={t('executiveOverview.planMetWeather.empty')} />
          </Panel>
          <Panel title={t('executiveOverview.delayCauses.title', { n: data.delayedReportCount })}>
            <HBars items={delayItems} max={Math.max(1, ...delayItems.map((item) => item.value))} emptyText={t('executiveOverview.delayCauses.empty')} />
          </Panel>
          <Panel title={t('executiveOverview.stock.title')} caption={stockCaption || undefined}>
            <StockTable materials={data.stockByMaterial} />
          </Panel>
          <Panel
            title={t('executiveOverview.problems.title')}
            caption={
              data.latestProblems.length
                ? t('executiveOverview.problems.caption', { n: data.problemReportCount, from: reportsSince, shown: Math.min(PROBLEMS_SHOWN, data.latestProblems.length) })
                : undefined
            }
          >
            <ProblemsList problems={data.latestProblems} since={reportsSince} stageName={(code) => stageNameOf(data, code, i18n.language, t)} />
          </Panel>
        </div>
      </div>
    </Stage>
  );
}
