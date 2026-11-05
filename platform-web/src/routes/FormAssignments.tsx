import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { useAssignmentFormat } from '../components/AssignmentStatus';
import { DateField, todayIso } from '../components/DateField';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FormAssignmentDto, FormSummaryDto, UserSummaryDto } from '../types';

interface FormAssignmentsProps {
  token: string;
}

const inputClass = 'w-full rounded border border-border bg-bg px-2 py-1.5 text-sm text-ink focus:border-accent focus:outline-none';
const sectionClass = 'rounded border border-border bg-panel p-4 shadow-recessed sm:p-5';
const headingClass = 'mb-3 text-label font-medium uppercase tracking-wider text-ink-soft';
const thClass = 'whitespace-nowrap border-b border-border px-2 py-2 text-start text-label font-medium uppercase tracking-wider text-ink-soft';
const tdClass = 'border-b border-border px-2 py-2 align-top';

/**
 * Administrators: assign a form to users to fill every working day, and see the assignments
 * (and stop one). Who filled what on a given day is its own page, routes/AssignmentTracking.tsx.
 */
export function FormAssignments({ token }: FormAssignmentsProps) {
  const { t } = useTranslation();
  const format = useAssignmentFormat();
  const today = todayIso();

  const [forms, setForms] = useState<FormSummaryDto[]>([]);
  const [users, setUsers] = useState<UserSummaryDto[]>([]);
  const [assignments, setAssignments] = useState<FormAssignmentDto[] | null>(null);
  const [loadError, setLoadError] = useErrorMessage();

  // New assignment
  const [formId, setFormId] = useState('');
  const [userIds, setUserIds] = useState<Set<string>>(new Set());
  const [startDate, setStartDate] = useState(today);
  const [endDate, setEndDate] = useState('');
  const [note, setNote] = useState('');
  const [assigning, setAssigning] = useState(false);
  const [assignError, setAssignError] = useErrorMessage();
  const [assignResult, setAssignResult] = useState<{ created: number; alreadyAssigned: string[] } | null>(null);

  const [stoppingId, setStoppingId] = useState<string | null>(null);
  const [stopError, setStopError] = useErrorMessage();

  const [refreshTick, setRefreshTick] = useState(0);

  useEffect(() => {
    Promise.all([api.forms.list(token), api.users.list(token), api.assignments.list(token)])
      .then(([f, u, a]) => {
        setForms(f.filter((form) => form.status === 'Published'));
        setUsers(u.filter((user) => user.isActive));
        setAssignments(a);
      })
      .catch((err) => setLoadError({ err, fallbackKey: 'assignments.admin.loadError' }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, refreshTick]);

  function toggleUser(id: string) {
    setUserIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function handleAssign(event: React.FormEvent) {
    event.preventDefault();
    setAssignError(null);
    setAssignResult(null);
    if (!formId || userIds.size === 0 || !startDate) {
      setAssignError({ key: 'assignments.admin.chooseFormAndUsers' });
      return;
    }
    setAssigning(true);
    try {
      const result = await api.assignments.create(token, {
        formDefinitionId: formId,
        userIds: [...userIds],
        startDate,
        endDate: endDate || null,
        note: note.trim() || null,
      });
      setAssignResult(result);
      setUserIds(new Set());
      setNote('');
      setRefreshTick((tick) => tick + 1);
    } catch (err) {
      setAssignError({ err, fallbackKey: 'assignments.admin.assignError' });
    } finally {
      setAssigning(false);
    }
  }

  async function handleStop(id: string) {
    setStopError(null);
    setStoppingId(id);
    try {
      await api.assignments.stop(token, id);
      setRefreshTick((tick) => tick + 1);
    } catch (err) {
      setStopError({ err, fallbackKey: 'assignments.admin.stopError' });
    } finally {
      setStoppingId(null);
    }
  }

  if (loadError) return <p className="text-sm text-danger">{loadError}</p>;
  if (!assignments)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
      </div>
    );

  return (
    <div className="max-w-5xl space-y-6">
      <div>
        <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{t('assignments.admin.title')}</h2>
        <p className="mt-1 text-sm text-ink-soft">{t('assignments.admin.intro')}</p>
        <Link
          to="/admin/assignments/status"
          className="mt-3 inline-block rounded bg-accent px-4 py-1.5 text-sm font-medium text-accent-ink hover:opacity-90"
        >
          {t('assignments.admin.openTracking')}
        </Link>
      </div>

      {/* ------------------------------------------------------------- new assignment */}
      <form onSubmit={handleAssign} className={sectionClass}>
        <h3 className={headingClass}>{t('assignments.admin.newTitle')}</h3>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="space-y-3">
            <div>
              <label htmlFor="assign-form" className="mb-1 block text-xs text-ink-soft">
                {t('assignments.admin.formLabel')}
              </label>
              <select id="assign-form" className={inputClass} value={formId} onChange={(e) => setFormId(e.target.value)}>
                <option value="">{t('common.select')}</option>
                {forms.map((form) => (
                  <option key={form.id} value={form.id}>
                    {form.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label htmlFor="assign-start" className="mb-1 block text-xs text-ink-soft">
                  {t('assignments.admin.startDate')}
                </label>
                <DateField id="assign-start" value={startDate} onChange={setStartDate} required className={inputClass} />
              </div>
              <div>
                <label htmlFor="assign-end" className="mb-1 block text-xs text-ink-soft">
                  {t('assignments.admin.endDate')}
                </label>
                <DateField id="assign-end" value={endDate} onChange={setEndDate} required={false} className={inputClass} />
                {endDate && (
                  <button type="button" onClick={() => setEndDate('')} className="mt-1 text-xs text-ink-soft hover:text-ink">
                    {t('assignments.admin.noEnd')}
                  </button>
                )}
              </div>
            </div>
            <div>
              <label htmlFor="assign-note" className="mb-1 block text-xs text-ink-soft">
                {t('assignments.admin.note')}
              </label>
              <input id="assign-note" className={inputClass} maxLength={400} value={note} onChange={(e) => setNote(e.target.value)} />
            </div>
          </div>
          <fieldset>
            <legend className="mb-1 block text-xs text-ink-soft">{t('assignments.admin.usersLabel')}</legend>
            <div className="max-h-56 space-y-1 overflow-y-auto rounded border border-border bg-bg p-2">
              {users.map((user) => (
                <label key={user.id} className="flex cursor-pointer items-center gap-2 rounded px-1 py-0.5 text-sm text-ink hover:bg-border/40">
                  <input type="checkbox" checked={userIds.has(user.id)} onChange={() => toggleUser(user.id)} className="bg-panel" />
                  <span dir="auto">{user.displayName}</span>
                  <span className="truncate text-xs text-ink-soft" dir="ltr">
                    {user.email}
                  </span>
                  {!user.phoneNumber && !user.telegramLinked && <span className="ms-auto shrink-0 text-xs text-danger">{t('assignments.admin.noPhone')}</span>}
                </label>
              ))}
            </div>
          </fieldset>
        </div>
        {assignError && <p className="mt-3 text-sm text-danger">{assignError}</p>}
        {assignResult && (
          <p className="mt-3 text-sm text-success">
            {t('assignments.admin.created', { n: assignResult.created })}
            {assignResult.alreadyAssigned.length > 0 && (
              <span className="ms-2 text-ink-soft">
                {t('assignments.admin.alreadyAssigned', { names: assignResult.alreadyAssigned.join('، ') })}
              </span>
            )}
          </p>
        )}
        <button
          type="submit"
          disabled={assigning}
          className="mt-4 rounded bg-accent px-4 py-1.5 text-sm font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-50"
        >
          {assigning ? t('assignments.admin.assigning') : t('assignments.admin.assign')}
        </button>
      </form>

      {/* ------------------------------------------------------------ assignments list */}
      <section className={sectionClass}>
        <h3 className={headingClass}>{t('assignments.admin.listTitle')}</h3>
        {stopError && <p className="mb-2 text-sm text-danger">{stopError}</p>}
        {assignments.length === 0 ? (
          <p className="text-sm text-ink-soft">{t('assignments.admin.noAssignments')}</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr>
                  <th className={thClass}>{t('assignments.user')}</th>
                  <th className={thClass}>{t('assignments.form')}</th>
                  <th className={thClass}>{t('assignments.admin.period')}</th>
                  <th className={thClass}>{t('assignments.assignedAt')}</th>
                  <th className={thClass}>{t('assignments.statusLabel')}</th>
                  <th className={thClass} />
                </tr>
              </thead>
              <tbody>
                {assignments.map((a) => (
                  <tr key={a.id} className={a.isActive ? '' : 'opacity-60'}>
                    <td className={tdClass} dir="auto">
                      {a.userName}
                    </td>
                    <td className={tdClass}>
                      <span dir="auto">{a.formName}</span>
                      {a.note && (
                        <span className="block text-xs text-ink-soft" dir="auto">
                          {a.note}
                        </span>
                      )}
                    </td>
                    <td className={`${tdClass} whitespace-nowrap`}>
                      {a.endDate
                        ? t('assignments.admin.until', { start: format.date(a.startDate), end: format.date(a.endDate) })
                        : t('assignments.admin.from', { date: format.date(a.startDate) })}
                      <span className="block text-xs text-ink-soft">
                        {t('assignments.admin.record', { filled: a.daysFilled, due: a.daysDue })}
                      </span>
                    </td>
                    <td className={`${tdClass} whitespace-nowrap`}>
                      {format.dateTime(a.assignedAtUtc)}
                      {a.assignedByName && (
                        <span className="block text-xs text-ink-soft">{t('assignments.assignedBy', { name: a.assignedByName })}</span>
                      )}
                    </td>
                    <td className={tdClass}>
                      <span className={`text-label uppercase tracking-wider ${a.isActive ? 'text-success' : 'text-ink-soft'}`}>
                        {a.isActive ? t('assignments.admin.active') : t('assignments.admin.ended')}
                      </span>
                    </td>
                    <td className={`${tdClass} text-end`}>
                      {a.isActive && (
                        <button
                          type="button"
                          onClick={() => handleStop(a.id)}
                          disabled={stoppingId === a.id}
                          className="text-label uppercase tracking-wide text-danger hover:underline disabled:opacity-50"
                        >
                          {stoppingId === a.id ? t('assignments.admin.stopping') : t('assignments.admin.stop')}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

    </div>
  );
}
