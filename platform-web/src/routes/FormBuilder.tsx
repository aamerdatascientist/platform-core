import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { AddFieldForm } from '../components/AddFieldForm';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { StatusLed } from '../components/StatusLed';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { FieldEditorRow } from '../components/FieldEditorRow';
import type { FieldDefinitionDto, FormDefinitionDto, FormSummaryDto, RoleDto, UserSummaryDto } from '../types';

/**
 * Which version's fields the builder shows and edits. This has to agree exactly with the
 * backend's FormEditTargetResolver.ResolveFieldEditTarget, or the builder would display one
 * version's fields while every edit landed on another: once a form is published, edits go to
 * the published version (live, DDL and all) even if some older draft is still lying open.
 * Before a form's first publish, its draft is the only thing there is.
 */
function resolveEditableFields(def: FormDefinitionDto): FieldDefinitionDto[] {
  const version = def.status === 'Published' ? def.publishedVersion : def.draftVersion;
  return version?.fields ?? [];
}

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

  /**
   * Safe operation: reorder. Sends the whole ordered list of ACTIVE field ids with one
   * neighbour swapped - the API rejects a partial list rather than half-applying it, so the
   * list is rebuilt from what's on screen each time rather than tracked separately.
   */
  async function handleMoveField(fieldId: string, direction: -1 | 1) {
    if (!formId || !formDefinition) return;
    const activeIds = resolveEditableFields(formDefinition)
      .filter((f) => f.isActive)
      .map((f) => f.id);
    const from = activeIds.indexOf(fieldId);
    const to = from + direction;
    if (from < 0 || to < 0 || to >= activeIds.length) return;

    [activeIds[from], activeIds[to]] = [activeIds[to], activeIds[from]];

    try {
      await api.forms.reorderFields(token, formId, activeIds);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.reorderError' });
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

  if (error && !formDefinition) return <p className="text-sm text-danger">{error}</p>;
  if (!formDefinition)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
      </div>
    );

  const hasDraft = formDefinition.draftVersion !== null;
  const isPublished = formDefinition.status === 'Published';
  const fields = resolveEditableFields(formDefinition);
  const activeFields = fields.filter((f) => f.isActive);
  const accessChanged =
    pendingRoleIds.size !== formDefinition.allowedRoleIds.length ||
    formDefinition.allowedRoleIds.some((id) => !pendingRoleIds.has(id)) ||
    pendingUserIds.size !== formDefinition.allowedUserIds.length ||
    formDefinition.allowedUserIds.some((id) => !pendingUserIds.has(id));

  return (
    <div className="max-w-3xl space-y-8">
      <div>
        <div className="flex items-center gap-3">
          <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{formDefinition.name}</h2>
          <StatusLed label={t(`formStatus.${formDefinition.status}`)} tone={isPublished ? 'success' : 'accent'} />
                  </div>
        <p className="mt-1 font-mono text-xs text-ink-soft">
          {formDefinition.code} · {formDefinition.moduleName}
        </p>
      </div>

      <div className="border border-border rounded bg-panel p-4 shadow-recessed">
        <h3 className="mb-1 text-[11px] font-medium uppercase tracking-wider text-ink-soft">{t('formBuilder.access')}</h3>
        <p className="mb-3 text-sm text-ink-soft">
          {formDefinition.allowedRoleIds.length === 0 && formDefinition.allowedUserIds.length === 0
            ? t('formBuilder.accessOpenDescription')
            : t('formBuilder.accessRestrictedDescription')}
        </p>

        <p className="mb-1 text-xs font-medium text-ink-soft">{t('formBuilder.byRole')}</p>
        <div className="mb-4 flex flex-wrap gap-3">
          {roles.length === 0 ? (
            <span className="text-sm text-ink-soft">{t('formBuilder.noRolesYet')}</span>
          ) : (
            roles.map((r) => (
              <label key={r.id} className="flex items-center gap-1.5 text-sm text-ink">
                <input type="checkbox" checked={pendingRoleIds.has(r.id)} onChange={() => toggleRoleInEditor(r.id)} />
                {r.name}
              </label>
            ))
          )}
        </div>

        <p className="mb-1 text-xs font-medium text-ink-soft">{t('formBuilder.byUser')}</p>
        <div className="mb-3 flex flex-wrap gap-3">
          {users.length === 0 ? (
            <span className="text-sm text-ink-soft">{t('formBuilder.noUsersYet')}</span>
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
          className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
        >
          {savingAccess ? t('formBuilder.savingAccess') : t('formBuilder.saveAccess')}
        </button>
      </div>

      {isPublished && (
        <p className="border border-border rounded bg-panel p-3 text-sm text-ink-soft shadow-recessed">
          {t('formBuilder.liveEditNote')}
        </p>
      )}

      <div>
        <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-soft">
          {t('formBuilder.fieldsCount', { count: activeFields.length })}
        </h3>
        <div className="space-y-1">
          {fields.map((f) => (
            <FieldEditorRow
              key={f.id}
              token={token}
              formId={formId!}
              field={f}
              isLive={isPublished}
              isFirst={activeFields[0]?.id === f.id}
              isLast={activeFields[activeFields.length - 1]?.id === f.id}
              lookupTargets={allForms.filter((form) => form.id !== formId)}
              onChanged={load}
              onMove={handleMoveField}
            />
          ))}
          {fields.length === 0 && <p className="text-sm text-ink-soft">{t('formBuilder.noFieldsYet')}</p>}
        </div>
      </div>

      {(hasDraft || isPublished) && (
        <>
          <div>
            <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-soft">
              {t('formBuilder.addAField')}
            </h3>
            <AddFieldForm
              token={token}
              formId={formId!}
              lookupTargets={allForms.filter((f) => f.id !== formId)}
              onAdded={load}
            />
          </div>

          {!isPublished && (
          <div className="border-t border-border rounded pt-6">
            {error && <p className="mb-2 text-sm text-danger">{error}</p>}
            <button
              onClick={handlePublish}
              disabled={publishing || fields.length === 0}
              className="bg-accent rounded px-4 py-2 text-sm font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-50"
            >
              {publishing ? t('formBuilder.publishing') : t('formBuilder.publishForm')}
            </button>
            <p className="mt-2 text-xs text-ink-soft">{t('formBuilder.publishNote')}</p>
          </div>
          )}
        </>
      )}

      {isPublished && (
        <button
          onClick={() => navigate(`/forms/${formId}`)}
          className="text-xs uppercase tracking-wide text-ink-soft hover:text-ink"
        >
          {t('formBuilder.goFillOutForm')}
        </button>
      )}

      <div className="border-t border-border rounded pt-6">
        {error && !confirmingDelete && <p className="mb-2 text-sm text-danger">{error}</p>}
        {!confirmingDelete ? (
          <button
            onClick={() => setConfirmingDelete(true)}
            className="text-[11px] uppercase tracking-wide text-danger hover:opacity-70"
          >
            {t('formBuilder.deleteThisForm')}
          </button>
        ) : (
          <div className="border border-danger rounded bg-panel p-3 shadow-recessed">
            <p className="mb-2 text-sm text-ink">
              {formDefinition.status === 'Published'
                ? t('formBuilder.deleteConfirmPublished')
                : t('formBuilder.deleteConfirmDraft')}
            </p>
            {error && <p className="mb-2 text-sm text-danger">{error}</p>}
            <div className="flex gap-2">
              <button
                onClick={handleDelete}
                disabled={deleting}
                className="bg-danger rounded px-3 py-1.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {deleting ? t('formBuilder.deleting') : t('formBuilder.yesDeleteIt')}
              </button>
              <button onClick={() => setConfirmingDelete(false)} className="px-3 py-1.5 text-sm text-ink-soft">
                {t('common.cancel')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
