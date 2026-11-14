import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate } from 'react-router-dom';
import { api } from '../api/client';
import { Icon } from '../components/icons';
import { Card, EmptyState, ErrorState, LoadingState } from '../components/ui';
import type { StageProjectSummaryDto } from '../types';

/**
 * Stage progress from the menu (admin flows F5-6, administrators): a plain list of projects,
 * each opening its stage progress page. With exactly one project it goes straight there.
 * Rows are the Forms page's list rows.
 */
export function ProjectChooser({ token }: { token: string }) {
  const { t } = useTranslation();
  const [projects, setProjects] = useState<StageProjectSummaryDto[] | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(() => {
    setFailed(false);
    api.stageProgress.projects(token).then(setProjects).catch(() => setFailed(true));
  }, [token]);
  useEffect(load, [load]);

  if (failed) return <ErrorState message={t('nav.projectsLoadError')} onRetry={load} />;
  if (!projects) return <LoadingState />;
  if (projects.length === 1) return <Navigate to={`/admin/projects/${projects[0].id}/progress`} replace />;

  return (
    <div className="mx-auto flex w-full max-w-[720px] flex-col gap-5">
      <div className="flex flex-col gap-1">
        <span className="text-[14px] text-ink-soft">{t('nav.stageProgress')}</span>
        <h1 className="text-[26px] font-extrabold text-ink">{t('nav.chooseProject')}</h1>
      </div>
      {projects.length === 0 ? (
        <EmptyState title={t('nav.noProjects')} />
      ) : (
        <Card className="divide-y divide-divider overflow-hidden">
          {projects.map((p) => (
            <Link key={p.id} to={`/admin/projects/${p.id}/progress`} className="flex min-h-[56px] items-center gap-3 px-[18px] text-[16px] font-medium text-ink hover:bg-field-muted">
              <span className="flex min-w-0 flex-1 flex-col">
                <bdi className="font-bold">{p.code}</bdi>
                {p.name && p.name !== p.code && (
                  <bdi className="text-[13px] font-normal text-ink-soft">{p.name}</bdi>
                )}
              </span>
              {p.status && <span className="text-[13px] text-ink-soft">{t(`executiveOverview.projectStatus.${p.status}`, p.status)}</span>}
              <Icon name="chevronForward" size={18} className="text-ink-soft" />
            </Link>
          ))}
        </Card>
      )}
    </div>
  );
}
