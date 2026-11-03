import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { AssignmentStatusBadge, useAssignmentFormat } from '../components/AssignmentStatus';
import { DateField, todayIso } from '../components/DateField';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { AssignmentDayDto, AssignmentDayStatus, FormSummaryDto, UserSummaryDto } from '../types';

interface AssignmentTrackingProps {
  token: string;
}

const inputClass = 'w-full rounded border border-border bg-bg px-2 py-1.5 text-sm text-ink focus:border-accent focus:outline-none';
const thClass = 'whitespace-nowrap border-b border-border px-2 py-2 text-start text-label font-medium uppercase tracking-wider text-ink-soft';
const tdClass = 'border-b border-border px-2 py-2 align-top';

/**
 * Administrators: one day's assigned forms - who was due to fill what, whether they did, and
 * when. Today by default; the user, form and status filters narrow the list.
 */
export function AssignmentTracking({ token }: AssignmentTrackingProps) {
  const { t } = useTranslation();
  const format = useAssignmentFormat();
  const today = todayIso();

  const [date, setDate] = useState(today);
  const [forms, setForms] = useState<FormSummaryDto[]>([]);
  const [users, setUsers] = useState<UserSummaryDto[]>([]);
  const [filterUser, setFilterUser] = useState('');
  const [filterForm, setFilterForm] = useState('');
  const [filterStatus, setFilterStatus] = useState<AssignmentDayStatus | ''>('');
  const [days, setDays] = useState<AssignmentDayDto[] | null>(null);
  const [error, setError] = useErrorMessage();

  useEffect(() => {
    Promise.all([api.forms.list(token), api.users.list(token)])
      .then(([f, u]) => {
        setForms(f.filter((form) => form.status === 'Published'));
        setUsers(u.filter((user) => user.isActive));
      })
      .catch(() => {});
  }, [token]);

  useEffect(() => {
    if (!date) return;
    setDays(null);
    api.assignments
      .days(token, { from: date, to: date, userId: filterUser || null, formId: filterForm || null })
      .then((result) => {
        setDays(result);
        setError(null);
      })
      .catch((err) => setError({ err, fallbackKey: 'assignments.admin.loadError' }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, date, filterUser, filterForm]);

  const shown = useMemo(() => (days ?? []).filter((day) => !filterStatus || day.status === filterStatus), [days, filterStatus]);
  const counts = useMemo(() => {
    const all = days ?? [];
    return {
      filled: all.filter((d) => d.status === 'filled').length,
      missed: all.filter((d) => d.status === 'missed').length,
      pending: all.filter((d) => d.status === 'pending').length,
    };
  }, [days]);
  const isFriday = new Date(`${date}T00:00:00Z`).getUTCDay() === 5;

  return (
    <div className="max-w-4xl space-y-4">
      <div>
        <Link to="/admin/assignments" className="text-xs text-ink-soft hover:text-ink">
          {t('assignments.tracking.back')}
        </Link>
        <h2 className="mt-1 font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">
          {t('assignments.tracking.title')}
        </h2>
      </div>

      <div className="rounded border border-border bg-panel p-4 shadow-recessed sm:p-5">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <label htmlFor="track-date" className="mb-1 block text-xs text-ink-soft">
              {t('assignments.tracking.date')}
            </label>
            <DateField id="track-date" value={date} onChange={setDate} required className={inputClass} maxDate={today} />
          </div>
          <div>
            <label htmlFor="track-user" className="mb-1 block text-xs text-ink-soft">
              {t('assignments.user')}
            </label>
            <select id="track-user" className={inputClass} value={filterUser} onChange={(e) => setFilterUser(e.target.value)}>
              <option value="">{t('assignments.admin.allUsers')}</option>
              {users.map((user) => (
                <option key={user.id} value={user.id}>
                  {user.displayName}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="track-form" className="mb-1 block text-xs text-ink-soft">
              {t('assignments.form')}
            </label>
            <select id="track-form" className={inputClass} value={filterForm} onChange={(e) => setFilterForm(e.target.value)}>
              <option value="">{t('assignments.admin.allForms')}</option>
              {forms.map((form) => (
                <option key={form.id} value={form.id}>
                  {form.name}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="track-status" className="mb-1 block text-xs text-ink-soft">
              {t('assignments.statusLabel')}
            </label>
            <select
              id="track-status"
              className={inputClass}
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value as AssignmentDayStatus | '')}
            >
              <option value="">{t('assignments.admin.allStatuses')}</option>
              {(['filled', 'missed', 'pending'] as const).map((status) => (
                <option key={status} value={status}>
                  {t(`assignments.status.${status}`)}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>

      <section className="rounded border border-border bg-panel p-4 shadow-recessed sm:p-5">
        <h3 className="mb-1 text-label font-medium uppercase tracking-wider text-ink-soft">{format.day(date)}</h3>
        {error ? (
          <p className="text-sm text-danger">{error}</p>
        ) : !days ? (
          <div className="flex items-center gap-2">
            <LoadingSpinner size="sm" />
            <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
          </div>
        ) : days.length === 0 ? (
          <p className="text-sm text-ink-soft">{isFriday ? t('assignments.tracking.notWorkingDay') : t('assignments.admin.noDays')}</p>
        ) : (
          <>
            <p className="mb-2 text-xs text-ink-soft">{t('assignments.admin.summary', counts)}</p>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr>
                    <th className={thClass}>{t('assignments.user')}</th>
                    <th className={thClass}>{t('assignments.form')}</th>
                    <th className={thClass}>{t('assignments.statusLabel')}</th>
                    <th className={thClass}>{t('assignments.filledAt')}</th>
                    <th className={thClass}>{t('assignments.assignedAt')}</th>
                  </tr>
                </thead>
                <tbody>
                  {shown.map((day) => (
                    <tr key={`${day.assignmentId}-${day.date}`}>
                      <td className={tdClass} dir="auto">
                        {day.userName}
                      </td>
                      <td className={tdClass} dir="auto">
                        {day.formName}
                      </td>
                      <td className={`${tdClass} whitespace-nowrap`}>
                        <AssignmentStatusBadge status={day.status} />
                      </td>
                      <td className={`${tdClass} whitespace-nowrap`}>
                        {day.filledAtUtc ? format.time(day.filledAtUtc) : t('common.empty')}
                        {day.submissionCount > 1 && (
                          <span className="block text-xs text-ink-soft">{t('assignments.times', { n: day.submissionCount })}</span>
                        )}
                      </td>
                      <td className={`${tdClass} whitespace-nowrap text-ink-soft`}>
                        {format.dateTime(day.assignedAtUtc)}
                        {day.assignedByName && (
                          <span className="block text-xs">{t('assignments.assignedBy', { name: day.assignedByName })}</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>
    </div>
  );
}
