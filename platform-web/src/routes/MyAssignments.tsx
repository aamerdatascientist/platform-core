import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { AssignmentStatusBadge, useAssignmentFormat } from '../components/AssignmentStatus';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { AssignmentDayDto, MyAssignmentsDto } from '../types';

interface MyAssignmentsProps {
  token: string;
}

/**
 * The forms the signed-in user is assigned to fill every working day: today's, each with a
 * button to open the form, then the last two weeks. Filled or not is worked out on the server
 * from the form's own submissions, so filling the form is all it takes - nothing to tick here.
 */
export function MyAssignments({ token }: MyAssignmentsProps) {
  const { t } = useTranslation();
  const format = useAssignmentFormat();
  const [data, setData] = useState<MyAssignmentsDto | null>(null);
  const [error, setError] = useErrorMessage();

  useEffect(() => {
    api.assignments
      .mine(token)
      .then(setData)
      .catch((err) => setError({ err, fallbackKey: 'assignments.mine.loadError' }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  const sectionClass = 'rounded border border-border bg-panel p-4 shadow-recessed sm:p-5';
  const headingClass = 'mb-3 text-label font-medium uppercase tracking-wider text-ink-soft';

  if (error) return <p className="text-sm text-danger">{error}</p>;
  if (!data)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
      </div>
    );

  const today = data.days.filter((d) => d.date === data.today);
  const recent = data.days.filter((d) => d.date !== data.today);

  return (
    <div className="max-w-3xl space-y-6">
      <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{t('assignments.mine.title')}</h2>

      {data.days.length === 0 ? (
        <p className="text-sm text-ink-soft">{t('assignments.mine.none')}</p>
      ) : (
        <>
          <section className={sectionClass}>
            <h3 className={headingClass}>
              {t('assignments.mine.today')} · {format.day(data.today)}
            </h3>
            {today.length === 0 ? (
              <p className="text-sm text-ink-soft">{t('assignments.mine.noneToday')}</p>
            ) : (
              <ul className="space-y-2">
                {today.map((day) => (
                  <TodayRow key={day.assignmentId} day={day} />
                ))}
              </ul>
            )}
          </section>

          {recent.length > 0 && (
            <section className={sectionClass}>
              <h3 className={headingClass}>{t('assignments.mine.recent')}</h3>
              <ul className="divide-y divide-border">
                {recent.map((day) => (
                  <li key={`${day.assignmentId}-${day.date}`} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 py-2">
                    <div className="min-w-0">
                      <span className="text-sm text-ink">{format.day(day.date)}</span>
                      <span className="ms-2 text-sm text-ink-soft" dir="auto">
                        {day.formName}
                      </span>
                    </div>
                    <div className="flex items-center gap-3">
                      {day.filledAtUtc && <span className="text-xs text-ink-soft">{format.time(day.filledAtUtc)}</span>}
                      <AssignmentStatusBadge status={day.status} />
                    </div>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </>
      )}
    </div>
  );
}

function TodayRow({ day }: { day: AssignmentDayDto }) {
  const { t } = useTranslation();
  const format = useAssignmentFormat();
  const filled = day.status === 'filled';
  return (
    <li className="flex flex-wrap items-center justify-between gap-3 rounded border border-border bg-bg px-4 py-3">
      <div className="min-w-0">
        <p className="text-sm font-medium text-ink" dir="auto">
          {day.formName}
        </p>
        <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
          <AssignmentStatusBadge status={day.status} />
          {day.filledAtUtc && (
            <span className="text-xs text-ink-soft">
              {format.time(day.filledAtUtc)}
              {day.submissionCount > 1 && ` · ${t('assignments.times', { n: day.submissionCount })}`}
            </span>
          )}
        </div>
      </div>
      <Link
        to={`/forms/${day.formId}`}
        className={
          filled
            ? 'rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:border-ink'
            : 'rounded bg-accent px-3 py-1.5 text-sm font-medium text-accent-ink hover:opacity-90'
        }
      >
        {filled ? t('assignments.mine.fillAgain') : t('assignments.mine.fill')}
      </Link>
    </li>
  );
}
