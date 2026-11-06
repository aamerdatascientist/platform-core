import { useTranslation } from 'react-i18next';
import { Link, Navigate } from 'react-router-dom';
import { Icon } from '../components/icons';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useFormsByModule, useShell } from '../components/shell/ShellData';
import { Card, EmptyState, SectionHeading } from '../components/ui';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { useEffect } from 'react';

/**
 * The phone Forms tab (HANDOFF §5.1; not drawn - styled like "Other forms" on 02): one card per
 * module with its name as a 14px heading, a 56px row per form with a chevron. Someone who can
 * open only one form (project managers today) goes straight to it.
 */
export function FormsList() {
  const { t } = useTranslation();
  const { forms, formsError } = useShell();
  const groups = useFormsByModule();
  const [error, setError] = useErrorMessage();
  useEffect(() => {
    setError(formsError ? { err: formsError, fallbackKey: 'sidebar.loadFormsError' } : null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formsError]);

  if (error) return <p className="text-[15px] text-danger">{error}</p>;
  if (!forms) return <LoadingSpinner />;

  const all = groups.flatMap((g) => g.forms);
  if (all.length === 1) return <Navigate to={`/forms/${all[0].id}`} replace />;

  return (
    <div className="flex flex-col gap-5">
      <h1 className="text-[26px] font-extrabold text-ink">{t('nav.forms')}</h1>
      {all.length === 0 && <EmptyState title={t('sidebar.noForms')} />}
      {groups.map((group) => (
        <section key={group.label} className="flex flex-col gap-2">
          <SectionHeading>{group.label}</SectionHeading>
          <Card className="divide-y divide-divider overflow-hidden">
            {group.forms.map((form) => (
              <Link key={form.id} to={`/forms/${form.id}`} className="flex min-h-[56px] items-center gap-3 px-5 text-[17px] text-ink hover:bg-field-muted">
                <span className="flex-1"><bdi>{form.name}</bdi></span>
                <Icon name="chevronForward" size={18} className="text-ink-soft" />
              </Link>
            ))}
          </Card>
        </section>
      ))}
    </div>
  );
}
