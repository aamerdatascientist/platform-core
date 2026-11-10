import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { formatCalendarDate } from '../forms/fieldValues';
import { pickLanguage } from '../forms/localized';
import { Icon } from '../components/icons';
import { Button, ErrorState, IconButton, LoadingState, Pill, TableCard, tdClass, thClass } from '../components/ui';
import type { ProjectProgressDto, StageUnitDto } from '../types';

/**
 * A project's stage progress (2026-11-10, administrators): the planned range, days earned, and
 * every unit - stage, step and floor or side - with its days and dates. A finished unit can be
 * reopened here, so it can be reported again.
 */
export function ProjectProgress({ token }: { token: string }) {
  const { projectId = '' } = useParams();
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const lang = i18n.language;
  const [data, setData] = useState<ProjectProgressDto | null>(null);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useErrorMessage();

  const load = useCallback(() => {
    setFailed(false);
    api.stageProgress.project(token, projectId).then(setData).catch(() => setFailed(true));
  }, [token, projectId]);
  useEffect(load, [load]);

  const stages = useMemo(() => {
    const groups = new Map<string, StageUnitDto[]>();
    for (const u of data?.units ?? []) groups.set(u.stageCode, [...(groups.get(u.stageCode) ?? []), u]);
    return [...groups.values()];
  }, [data]);

  if (failed) return <ErrorState message={t('stageProgress.loadError')} onRetry={load} />;
  if (!data) return <LoadingState />;

  const date = (d: string | null) => (d ? formatCalendarDate(d, lang) : '—');
  const pct = (n: number) => `${Math.round(n * 100)}%`;
  const reopen = async (u: StageUnitDto) => {
    setBusy(u.key);
    setError(null);
    try {
      await api.stageProgress.reopen(token, data.projectId, { stageCode: u.stageCode, substepCode: u.substepCode, levelId: u.levelId, side: u.side, reason: null });
      load();
    } catch (err) {
      setError({ err, fallbackKey: 'stageProgress.reopenFailed' });
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="mx-auto flex max-w-[1080px] flex-col gap-5">
      <header className="flex items-center gap-2">
        <IconButton label={t('steps.back')} onClick={() => (window.history.length > 1 ? navigate(-1) : navigate('/'))}>
          <Icon name="arrowBack" size={22} />
        </IconButton>
        <div className="flex min-w-0 flex-col">
          <h1 className="truncate text-[22px] font-extrabold text-ink">
            <bdi>{data.name || data.code}</bdi> · {t('stageProgress.title')}
          </h1>
          <span className="text-[14px] text-ink-soft">
            {t('stageProgress.summary', { earned: data.earnedDays, planned: data.plannedDays, pct: pct(data.workComplete) })}
          </span>
        </div>
      </header>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <Fact label={t('stageProgress.plannedRange')} value={t('stageProgress.daysRange', { fast: data.totalFastest, slow: data.totalSlowest })} />
        <Fact label={t('stageProgress.plannedFinish')} value={`${date(data.finishFastest)} – ${date(data.finishSlowest)}`} />
        <Fact label={t('stageProgress.targetFinish')} value={date(data.finishTarget)} />
      </div>
      {error && <p className="text-[14px] text-danger">{error}</p>}

      {stages.map((units) => {
        const first = units[0];
        const earned = units.reduce((n, u) => n + u.earnedDays, 0);
        const planned = units.reduce((n, u) => n + u.plannedDays, 0);
        return (
          <section key={first.stageCode} className="flex flex-col gap-2">
            <h2 className="flex items-center gap-2 text-[17px] font-bold text-ink">
              <bdi>{pickLanguage(first.stageNameAr, first.stageNameEn, lang)}</bdi>
              <span className="text-[14px] font-medium text-ink-soft">
                {earned} / {planned}
              </span>
            </h2>
            <TableCard>
              <table className="w-full text-[14px]">
                <thead>
                  <tr>
                    <th className={thClass}>{t('stageProgress.colUnit')}</th>
                    <th className={thClass}>{t('stageProgress.colDays')}</th>
                    <th className={thClass}>{t('stageProgress.colState')}</th>
                    <th className={thClass}>{t('stageProgress.colPlannedFinish')}</th>
                    <th className={thClass}>{t('stageProgress.colLate')}</th>
                    <th className={thClass} aria-label={t('stageProgress.colActions')} />
                  </tr>
                </thead>
                <tbody>
                  {units.map((u) => (
                    <tr key={u.key} data-unit={u.key}>
                      <td className={tdClass}>
                        <bdi>
                          {[pickLanguage(u.substepNameAr ?? '', u.substepNameEn, lang), pickLanguage(u.levelNameAr ?? '', u.levelNameEn, lang)].filter(Boolean).join(' · ') ||
                            t('stageProgress.wholeProject')}
                        </bdi>
                      </td>
                      <td className={tdClass}>
                        {u.earnedDays} / {u.plannedDays}
                      </td>
                      <td className={tdClass}>
                        {u.finished ? (
                          <Pill tone="success">{t('stageProgress.finished')}</Pill>
                        ) : u.earnedDays > 0 ? (
                          <Pill tone="warning">{t('stageProgress.inProgress')}</Pill>
                        ) : (
                          <Pill>{t('stageProgress.notStarted')}</Pill>
                        )}
                      </td>
                      <td className={tdClass}>{date(u.plannedFinish)}</td>
                      <td className={tdClass}>
                        {u.lateByDays == null ? '—' : u.lateByDays > 0 ? t('stageProgress.lateBy', { count: u.lateByDays }) : u.lateByDays < 0 ? t('stageProgress.earlyBy', { count: -u.lateByDays }) : t('stageProgress.onTime')}
                      </td>
                      <td className={tdClass}>
                        {u.finished && (
                          <Button size="compact" variant="link" disabled={busy !== null} onClick={() => void reopen(u)}>
                            {busy === u.key ? t('stageProgress.reopening') : t('stageProgress.reopen')}
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableCard>
          </section>
        );
      })}
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-0.5 rounded-ui-2xl border border-border bg-panel px-4 py-3">
      <span className="text-[13px] text-ink-soft">{label}</span>
      <span className="text-[16px] font-bold text-ink">{value}</span>
    </div>
  );
}
