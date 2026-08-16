import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { AddFieldForm } from '../components/AddFieldForm';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FormDefinitionDto, FormSummaryDto, RoleDto, UserSummaryDto } from '../types';

export function FormBuilder({ token }: { token: string }) {
  const { t } = useTranslation();
  const { formId } = useParams<{ formId: string }>();
  const navigate = useNavigate();

  const [formDefinition, setFormDefinition] = useState<FormDefinitionDto | null>(null);
  const [allForms, setAllForms] = useState<FormSummaryDto[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [users, setUsers] = useState<UserSummaryDto[]>([]);
  const [error, setError] = useErrorMessage();
  const [publishing, setPublishing] = useState(false);
  const [startingNewVersion, setStartingNewVersion] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const [pendingRoleIds, setPendingRoleIds] = useState<Set<string>>(new Set());
  const [pendingUserIds, setPendingUserIds] = useState<Set<string>>(new Set());
  const [savingAccess, setSavingAccess] = useState(false);

  useEffect(() => {
    if (!formId) return;
    setConfirmingDelete(false);
    load();
    api.forms.list(token).then(setAllForms);
    api.roles.list(token).then(setRoles).catch(() => setRoles([]));
    api.users.list(token).then(setUsers).catch(() => setUsers([]));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId]);

  async function load() {
    if (!formId) return;
    try {
      const def = await api.forms.get(token, formId);
      setFormDefinition(def);
      setPendingRoleIds(new Set(def.allowedRoleIds));
      setPendingUserIds(new Set(def.allowedUserIds));
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.loadError' });
    }
  }

  async function handleRemoveField(fieldId: string) {
    if (!formId) return;
    try {
      await api.forms.removeField(token, formId, fieldId);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.removeFieldError' });
    }
  }

  async function handlePublish() {
    if (!formId) return;
    setPublishing(true);
    setError(null);
    try {
      await api.forms.publish(token, formId);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.publishError' });
    } finally {
      setPublishing(false);
    }
  }

  async function handleStartNewVersion() {
    if (!formId) return;
    setStartingNewVersion(true);
    setError(null);
    try {
      await api.forms.startNewVersion(token, formId);
      await load();
    } catch (err) {
      if (err instanceof ApiError && err.code === 'form.startVersion.draftAlreadyOpen') {
        // Not a real failure - someone else (or another tab) already started one.
        // Reload so the field editor shows it, rather than surfacing an alarming error.
        await load();
      } else {
        setError({ err, fallbackKey: 'formBuilder.startVersionError' });
      }
    } finally {
      setStartingNewVersion(false);
    }
  }

  async function handleDelete() {
    if (!formId) return;
    setDeleting(true);
    setError(null);
    try {
      await api.forms.delete(token, formId);
      navigate('/builder');
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.deleteError' });
      setConfirmingDelete(false);
    } finally {
      setDeleting(false);
    }
  }

  function toggleRoleInEditor(roleId: string) {
    setPendingRoleIds((prev) => {
      const next = new Set(prev);
      if (next.has(roleId)) next.delete(roleId);
      else next.add(roleId);
      return next;
    });
  }

  function toggleUserInEditor(userId: string) {
    setPendingUserIds((prev) => {
      const next = new Set(prev);
      if (next.has(userId)) next.delete(userId);
      else next.add(userId);
      return next;
    });
  }

  async function handleSaveAccess() {
    if (!formId) return;
    setSavingAccess(true);
    setError(null);
    try {
      await Promise.all([
        api.forms.setAllowedRoles(token, formId, [...pendingRoleIds]),
        api.forms.setAllowedUsers(token, formId, [...pendingUserIds]),
      ]);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.accessError' });
    } finally {
      setSavingAccess(false);
    }
  }

  if (error && !formDefinition) return <p className="text-sm text-clay">{error}</p>;
  if (!formDefinition)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-muted">{t('common.loading')}</span>
      </div>
    );

  const hasDraft = formDefinition.draftVersion !== null;
  const isPublished = formDefinition.status === 'Published';
  const fields = (hasDraft ? formDefinition.draftVersion : formDefinition.publishedVersion)?.fields ?? [];
  const accessChanged =
    pendingRoleIds.size !== formDefinition.allowedRoleIds.length ||
    formDefinition.allowedRoleIds.some((id) => !pendingRoleIds.has(id)) ||
    pendingUserIds.size !== formDefinition.allowedUserIds.length ||
    formDefinition.allowedUserIds.some((id) => !pendingUserIds.has(id));

  return (
    <div className="max-w-3xl space-y-8">
      <div>
        <div className="flex items-center gap-3">
          <h2 className="font-display text-xl font-semibold text-ink">{formDefinition.name}</h2>
          <span
            className={`border px-2 py-0.5 text-[11px] uppercase tracking-wider ${
              isPublished ? 'border-moss text-moss' : 'border-signal-dark text-signal-dark'
            }`}
          >
            {t(`formStatus.${formDefinition.status}`)}
          </span>
          {isPublished && hasDraft && (
            <span className="border border-signal-dark px-2 py-0.5 text-[11px] uppercase tracking-wider text-signal-dark">
              {t('formBuilder.editingNewVersion')}
            </span>
          )}
        </div>
        <p className="mt-1 font-mono text-xs text-ink-muted">
          {formDefinition.code} · {formDefinition.moduleName}
        </p>
      </div>

      <div className="border border-line bg-white p-4">
        <h3 className="mb-1 text-[11px] font-medium uppercase tracking-wider text-ink-muted">{t('formBuilder.access')}</h3>
        <p className="mb-3 text-sm text-ink-muted">
          {formDefinition.allowedRoleIds.length === 0 && formDefinition.allowedUserIds.length === 0
            ? t('formBuilder.accessOpenDescription')
            : t('formBuilder.accessRestrictedDescription')}
        </p>

        <p className="mb-1 text-xs font-medium text-ink-muted">{t('formBuilder.byRole')}</p>
        <div className="mb-4 flex flex-wrap gap-3">
          {roles.length === 0 ? (
            <span className="text-sm text-ink-muted">{t('formBuilder.noRolesYet')}</span>
          ) : (
            roles.map((r) => (
              <label key={r.id} className="flex items-center gap-1.5 text-sm text-ink">
                <input type="checkbox" checked={pendingRoleIds.has(r.id)} onChange={() => toggleRoleInEditor(r.id)} />
                {r.name}
              </label>
            ))
          )}
        </div>

        <p className="mb-1 text-xs font-medium text-ink-muted">{t('formBuilder.byUser')}</p>
        <div className="mb-3 flex flex-wrap gap-3">
          {users.length === 0 ? (
            <span className="text-sm text-ink-muted">{t('formBuilder.noUsersYet')}</span>
          ) : (
            users.map((u) => (
              <label key={u.id} className="flex items-center gap-1.5 text-sm text-ink">
                <input type="checkbox" checked={pendingUserIds.has(u.id)} onChange={() => toggleUserInEditor(u.id)} />
                {u.displayName}
              </label>
            ))
          )}
        </div>

        <button
          onClick={handleSaveAccess}
          disabled={savingAccess || !accessChanged}
          className="bg-ink px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
        >
          {savingAccess ? t('formBuilder.savingAccess') : t('formBuilder.saveAccess')}
        </button>
      </div>

      {isPublished && !hasDraft && (
        <div className="flex items-center justify-between gap-3 border border-line bg-white p-3">
          <p className="text-sm text-ink-muted">{t('formBuilder.publishedNoDraftPrompt')}</p>
          <button
            onClick={handleStartNewVersion}
            disabled={startingNewVersion}
            className="shrink-0 bg-ink px-3 py-1.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
          >
            {startingNewVersion ? t('formBuilder.startingNewVersion') : t('formBuilder.addMoreFields')}
          </button>
        </div>
      )}

      {isPublished && hasDraft && (
        <p className="border border-line bg-white p-3 text-sm text-ink-muted">{t('formBuilder.editingPublishedNote')}</p>
      )}

      <div>
        <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-muted">
          {t('formBuilder.fieldsCount', { count: fields.length })}
        </h3>
        <div className="space-y-1">
          {fields.map((f) => (
            <div key={f.id} className="flex items-center justify-between border border-line bg-white px-3 py-2 text-sm">
              <span>
                <span className="font-medium text-ink">{f.label}</span>
                <span className="ms-2 font-mono text-xs text-ink-muted">
                  {f.code} · {t(`fieldType.${f.fieldType}`)}
                  {f.isRequired ? ` · ${t('formBuilder.required')}` : ''}
                </span>
              </span>
              {hasDraft && (
                <button
                  onClick={() => handleRemoveField(f.id)}
                  className="text-[11px] uppercase tracking-wide text-clay hover:opacity-70"
                >
                  {t('common.remove')}
                </button>
              )}
            </div>
          ))}
          {fields.length === 0 && <p className="text-sm text-ink-muted">{t('formBuilder.noFieldsYet')}</p>}
        </div>
      </div>

      {hasDraft && (
        <>
          <div>
            <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-muted">
              {t('formBuilder.addAField')}
            </h3>
            <AddFieldForm
              token={token}
              formId={formId!}
              lookupTargets={allForms.filter((f) => f.id !== formId)}
              onAdded={load}
            />
          </div>

          <div className="border-t border-line pt-6">
            {error && <p className="mb-2 text-sm text-clay">{error}</p>}
            <button
              onClick={handlePublish}
              disabled={publishing || fields.length === 0}
              className="bg-ink px-4 py-2 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
            >
              {publishing ? t('formBuilder.publishing') : t('formBuilder.publishForm')}
            </button>
            <p className="mt-2 text-xs text-ink-muted">{t('formBuilder.publishNote')}</p>
          </div>
        </>
      )}

      {isPublished && (
        <button
          onClick={() => navigate(`/forms/${formId}`)}
          className="text-xs uppercase tracking-wide text-ink-muted hover:text-ink"
        >
          {t('formBuilder.goFillOutForm')}
        </button>
      )}

      <div className="border-t border-line pt-6">
        {error && !confirmingDelete && <p className="mb-2 text-sm text-clay">{error}</p>}
        {!confirmingDelete ? (
          <button
            onClick={() => setConfirmingDelete(true)}
            className="text-[11px] uppercase tracking-wide text-clay hover:opacity-70"
          >
            {t('formBuilder.deleteThisForm')}
          </button>
        ) : (
          <div className="border border-clay bg-white p-3">
            <p className="mb-2 text-sm text-ink">
              {formDefinition.status === 'Published'
                ? t('formBuilder.deleteConfirmPublished')
                : t('formBuilder.deleteConfirmDraft')}
            </p>
            {error && <p className="mb-2 text-sm text-clay">{error}</p>}
            <div className="flex gap-2">
              <button
                onClick={handleDelete}
                disabled={deleting}
                className="bg-clay px-3 py-1.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {deleting ? t('formBuilder.deleting') : t('formBuilder.yesDeleteIt')}
              </button>
              <button onClick={() => setConfirmingDelete(false)} className="px-3 py-1.5 text-sm text-ink-muted">
                {t('common.cancel')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
