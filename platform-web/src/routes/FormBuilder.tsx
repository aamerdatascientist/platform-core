import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { AddFieldForm } from '../components/AddFieldForm';
import { FieldList } from '../components/builder/FieldList';
import type { BuilderStep } from '../components/builder/FieldList';
import { builderInput, FieldSettings } from '../components/builder/FieldSettings';
import { PhonePreview } from '../components/builder/PhonePreview';
import { Icon } from '../components/icons';
import { Button, EmptyState, ErrorState, InfoBanner, LoadingState, Sheet } from '../components/ui';
import { useLocalized } from '../forms/localized';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FieldDefinitionDto, FormDefinitionDto, FormSummaryDto, FormVersionDto, RoleDto, UserSummaryDto } from '../types';

/**
 * Which version the builder shows and edits. This has to agree exactly with the backend's
 * FormEditTargetResolver.ResolveFieldEditTarget, or the builder would display one version's
 * fields while every edit landed on another: once a form is published, edits go to the
 * published version (live, DDL and all) even if some older draft is still lying open. Before a
 * form's first publish, its draft is the only thing there is.
 */
function resolveEditableVersion(def: FormDefinitionDto): FormVersionDto | null {
  return (def.status === 'Published' ? def.publishedVersion : def.draftVersion) ?? null;
}

/**
 * Form builder (screen 21): header with the access summary and "Open form ↗", the live-edit
 * note, then three panes - fields by step, the selected field's settings, and a live phone
 * preview of that field's step. Below 1280px the preview drops under the other two.
 */
export function FormBuilder({ token }: { token: string }) {
  const { t } = useTranslation();
  const { formName, stepTitle } = useLocalized();
  const { formId } = useParams<{ formId: string }>();
  const navigate = useNavigate();

  const [form, setForm] = useState<FormDefinitionDto | null>(null);
  const [allForms, setAllForms] = useState<FormSummaryDto[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [users, setUsers] = useState<UserSummaryDto[]>([]);
  const [error, setError] = useErrorMessage();
  const [busy, setBusy] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Dialogs: access, form name, add (field or step), a step's titles.
  const [accessOpen, setAccessOpen] = useState(false);
  const [pendingRoleIds, setPendingRoleIds] = useState<Set<string>>(new Set());
  const [pendingUserIds, setPendingUserIds] = useState<Set<string>>(new Set());
  const [nameOpen, setNameOpen] = useState(false);
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [addOpen, setAddOpen] = useState<'none' | 'choose' | 'field'>('none');
  const [stepDialog, setStepDialog] = useState<{ id: string | null; titleAr: string; titleEn: string } | null>(null);
  // A repeatable section being added or edited (2026-11-10).
  const [sectionDialog, setSectionDialog] = useState<{ id: string | null; code: string; titleAr: string; titleEn: string; min: string; max: string } | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  useEffect(() => {
    if (!formId) return;
    setConfirmingDelete(false);
    setSelectedId(null);
    void load();
    api.forms.list(token).then(setAllForms).catch(() => setAllForms([]));
    api.roles.list(token).then(setRoles).catch(() => setRoles([]));
    api.users.list(token).then(setUsers).catch(() => setUsers([]));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId]);

  async function load() {
    if (!formId) return;
    try {
      const def = await api.forms.get(token, formId);
      setForm(def);
      setPendingRoleIds(new Set(def.allowedRoleIds));
      setPendingUserIds(new Set(def.allowedUserIds));
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.loadError' });
    }
  }

  const version = form ? resolveEditableVersion(form) : null;
  const fields = useMemo(() => [...(version?.fields ?? [])].sort((a, b) => a.displayOrder - b.displayOrder), [version]);
  const activeFields = fields.filter((f) => f.isActive);
  const archived = fields.filter((f) => !f.isActive);

  // Fields under their steps - the same grouping the form itself uses: no step (or a step that
  // no longer exists) means the first step; no steps at all means one step named after the form.
  const steps: BuilderStep[] = useMemo(() => {
    if (!form) return [];
    const defined = [...(version?.steps ?? [])].sort((a, b) => a.displayOrder - b.displayOrder);
    if (defined.length === 0) return [{ id: null, title: formName(form), titleAr: form.name, titleEn: form.nameEn ?? null, fields: activeFields }];
    const known = new Set(defined.map((d) => d.id));
    const stepOf = (f: FieldDefinitionDto) => (f.stepId && known.has(f.stepId) ? f.stepId : defined[0].id);
    return defined.map((d) => ({ id: d.id, title: stepTitle(d), titleAr: d.titleAr, titleEn: d.titleEn, fields: activeFields.filter((f) => stepOf(f) === d.id) }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form, version, formName, stepTitle]);

  // Keep a field selected: the first one until someone picks another.
  useEffect(() => {
    if (!selectedId || !fields.some((f) => f.id === selectedId)) setSelectedId(activeFields[0]?.id ?? fields[0]?.id ?? null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fields]);

  async function act(action: () => Promise<unknown>, fallbackKey: string) {
    setBusy(true);
    setError(null);
    try {
      await action();
      await load();
      return true;
    } catch (err) {
      setError({ err, fallbackKey });
      return false;
    } finally {
      setBusy(false);
    }
  }

  /** Safe: move a field to a step and position, then send the whole order (the API refuses a partial list). */
  function moveField(fieldId: string, stepId: string | null, beforeId: string | null) {
    if (!formId) return;
    const groups = steps.map((s) => ({ id: s.id, ids: s.fields.map((f) => f.id).filter((id) => id !== fieldId) }));
    const from = steps.find((s) => s.fields.some((f) => f.id === fieldId));
    const target = groups.find((g) => g.id === stepId) ?? groups[0];
    const at = beforeId ? target.ids.indexOf(beforeId) : -1;
    target.ids.splice(at < 0 ? target.ids.length : at, 0, fieldId);
    const order = groups.flatMap((g) => g.ids);
    const stepChanged = from && from.id !== target.id && target.id !== null;
    void act(async () => {
      if (stepChanged) await api.forms.setFieldStep(token, formId, fieldId, target.id);
      await api.forms.reorderFields(token, formId, order);
    }, 'formBuilder.reorderError');
  }

  function moveStep(stepId: string, direction: -1 | 1) {
    if (!formId) return;
    const ids = steps.map((s) => s.id!).filter(Boolean);
    const i = ids.indexOf(stepId);
    const j = i + direction;
    if (i < 0 || j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    void act(() => api.forms.reorderSteps(token, formId, ids), 'formBuilder.reorderError');
  }

  async function saveStep() {
    if (!formId || !stepDialog || !stepDialog.titleAr.trim()) return;
    const ok = await act(
      () =>
        stepDialog.id
          ? api.forms.updateStep(token, formId, stepDialog.id, stepDialog.titleAr.trim(), stepDialog.titleEn.trim())
          : api.forms.addStep(token, formId, stepDialog.titleAr.trim(), stepDialog.titleEn.trim()),
      'formBuilder.reorderError',
    );
    if (ok) setStepDialog(null);
  }

  async function deleteStep() {
    if (!formId || !stepDialog?.id) return;
    const id = stepDialog.id;
    if (await act(() => api.forms.deleteStep(token, formId, id), 'formBuilder.reorderError')) setStepDialog(null);
  }

  async function saveSection() {
    if (!formId || !sectionDialog || !sectionDialog.titleAr.trim()) return;
    const body = {
      titleAr: sectionDialog.titleAr.trim(),
      titleEn: sectionDialog.titleEn.trim() || null,
      minBlocks: Number(sectionDialog.min) || 0,
      maxBlocks: Number(sectionDialog.max) || 1,
    };
    const ok = await act(
      () =>
        sectionDialog.id
          ? api.forms.updateSection(token, formId, sectionDialog.id, body)
          : api.forms.addSection(token, formId, { ...body, code: sectionDialog.code.trim() }),
      'sectionsBuilder.saveError',
    );
    if (ok) setSectionDialog(null);
  }

  async function removeSection() {
    if (!formId || !sectionDialog?.id) return;
    const id = sectionDialog.id;
    if (await act(() => api.forms.removeSection(token, formId, id), 'sectionsBuilder.saveError')) setSectionDialog(null);
  }

  async function saveName() {
    if (!formId || !nameAr.trim()) return;
    if (await act(() => api.forms.updateName(token, formId, nameAr.trim(), nameEn.trim()), 'formBuilder.loadError')) setNameOpen(false);
  }

  async function saveAccess() {
    if (!formId) return;
    const ok = await act(
      () => Promise.all([api.forms.setAllowedRoles(token, formId, [...pendingRoleIds]), api.forms.setAllowedUsers(token, formId, [...pendingUserIds])]),
      'formBuilder.accessError',
    );
    if (ok) setAccessOpen(false);
  }

  async function publish() {
    if (!formId) return;
    setPublishing(true);
    await act(() => api.forms.publish(token, formId), 'formBuilder.publishError');
    setPublishing(false);
  }

  async function deleteForm() {
    if (!formId) return;
    setBusy(true);
    try {
      await api.forms.delete(token, formId);
      navigate('/builder');
    } catch (err) {
      setError({ err, fallbackKey: 'formBuilder.deleteError' });
      setConfirmingDelete(false);
    } finally {
      setBusy(false);
    }
  }

  if (error && !form) return <ErrorState message={error} onRetry={() => (setError(null), void load())} />;
  if (!form || !formId) return <LoadingState />;

  const isPublished = form.status === 'Published';
  const selected = fields.find((f) => f.id === selectedId) ?? null;
  const selectedStepIndex = selected ? steps.findIndex((s) => s.fields.some((f) => f.id === selected.id)) : -1;
  const moduleLabel = t(`moduleName.${form.moduleName}`, { defaultValue: form.moduleName });
  const accessLabel =
    form.allowedRoleIds.length === 0 && form.allowedUserIds.length === 0
      ? t('builderV2.accessEveryone')
      : t('builderV2.accessSummary', {
          summary: [
            ...roles.filter((r) => form.allowedRoleIds.includes(r.id)).map((r) => t(`roleName.${r.name}`, { defaultValue: r.name })),
            ...(form.allowedUserIds.length > 0 ? [t('builderV2.usersCount', { count: form.allowedUserIds.length })] : []),
          ].join(' + '),
        });
  // The preview renders the editable version as if it were the published one.
  const previewForm: FormDefinitionDto | null = version ? { ...form, publishedVersion: version } : null;
  const lookupTargets = allForms.filter((f) => f.id !== formId);
  const accessChanged =
    pendingRoleIds.size !== form.allowedRoleIds.length ||
    form.allowedRoleIds.some((id) => !pendingRoleIds.has(id)) ||
    pendingUserIds.size !== form.allowedUserIds.length ||
    form.allowedUserIds.some((id) => !pendingUserIds.has(id));

  return (
    <div className="flex flex-col gap-[18px]">
      {/* Header: breadcrumb, title + status pill + code; Access and Open form on the end side. */}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-[14px] text-ink-soft">
            {t('settings.formBuilder')} › {moduleLabel}
          </span>
          <div className="flex flex-wrap items-center gap-[10px]">
            <h1 className="text-[28px] font-extrabold text-ink">
              <bdi>{formName(form)}</bdi>
            </h1>
            <button
              type="button"
              aria-label={t('builderV2.editName')}
              onClick={() => {
                setNameAr(form.name);
                setNameEn(form.nameEn ?? '');
                setNameOpen(true);
              }}
              className="flex h-9 w-9 items-center justify-center rounded-ui-md text-ink-soft hover:bg-field-muted"
            >
              <Icon name="pencil" size={18} />
            </button>
            <span className={`rounded-ui-pill px-[10px] py-[3px] text-[12px] font-bold ${isPublished ? 'bg-success-soft text-success' : 'bg-accent-soft text-accent-strong'}`}>
              {t('builderV2.statusFields', { status: t(`formStatus.${form.status}`), count: activeFields.length })}
            </span>
            <span dir="ltr" className="font-mono text-[13px] text-ink-soft">
              {form.code}
            </span>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="desktop" variant="secondary" onClick={() => setAccessOpen(true)} icon={<Icon name="lock" size={16} />} className="!font-normal">
            {accessLabel}
          </Button>
          {isPublished && (
            <a href={`/forms/${formId}`} target="_blank" rel="noreferrer" className="flex h-[42px] items-center rounded-ui-lg border border-border bg-panel px-4 text-[15px] text-ink hover:border-ink-soft">
              {t('builderV2.openForm')}
            </a>
          )}
        </div>
      </div>

      {isPublished && (
        <div role="note">
          <InfoBanner>{t('formBuilder.liveEditNote')}</InfoBanner>
        </div>
      )}
      {error && (
        <p className="text-[14px] text-danger" role="alert">
          {error}
        </p>
      )}

      <div className="flex flex-wrap items-start gap-4">
        <FieldList
          steps={steps}
          archived={archived}
          selectedId={selectedId}
          lookupTargets={allForms}
          busy={busy}
          onSelect={setSelectedId}
          onMove={moveField}
          onEditStep={(s) => setStepDialog({ id: s.id, titleAr: s.titleAr, titleEn: s.titleEn ?? '' })}
          onMoveStep={moveStep}
          onAdd={() => setAddOpen('choose')}
        />
        {selected ? (
          <FieldSettings
            key={selected.id}
            token={token}
            formId={formId}
            field={selected}
            isLive={isPublished}
            stepLabel={selectedStepIndex >= 0 && steps[selectedStepIndex].id ? t('builderV2.stepHeading', { n: selectedStepIndex + 1, title: steps[selectedStepIndex].title }).split(' · ')[0] : null}
            lookupTargets={lookupTargets}
            controllableFields={activeFields.filter((o) => o.id !== selected.id && (o.fieldType === 'Dropdown' || o.fieldType === 'Boolean'))}
            filterableFields={activeFields.filter((o) => o.id !== selected.id)}
            onChanged={load}
          />
        ) : (
          <div className="flex-[999_1_380px]">
            <EmptyState title={t('formBuilder.noFieldsYet')} body={t('builderV2.selectField')} />
          </div>
        )}
        {previewForm && activeFields.length > 0 && (
          <PhonePreview token={token} form={previewForm} stepId={selectedStepIndex >= 0 ? steps[selectedStepIndex].id : null} highlightFieldId={selected?.isActive ? selected.id : null} />
        )}
      </div>

      {/* A never-published form: publish it (creates its table). Any form: delete it. */}
      <div className="flex flex-col gap-3 border-t border-divider pt-5">
        {!isPublished && form.draftVersion && (
          <div className="flex flex-col items-start gap-2">
            <Button size="desktop" disabled={publishing || activeFields.length === 0} onClick={publish}>
              {publishing ? t('formBuilder.publishing') : t('formBuilder.publishForm')}
            </Button>
            <p className="text-[13px] text-ink-soft">{t('formBuilder.publishNote')}</p>
          </div>
        )}
        {!confirmingDelete ? (
          <Button size="desktop" variant="link" onClick={() => setConfirmingDelete(true)} className="self-start !px-0 !text-danger">
            {t('formBuilder.deleteThisForm')}
          </Button>
        ) : (
          <div className="flex max-w-[640px] flex-col gap-3 rounded-ui-2xl border border-danger-border bg-panel p-[18px]">
            <p className="text-[14px] text-ink">{isPublished ? t('formBuilder.deleteConfirmPublished') : t('formBuilder.deleteConfirmDraft')}</p>
            <div className="flex gap-2">
              <Button size="desktop" variant="danger" disabled={busy} onClick={deleteForm}>
                {busy ? t('formBuilder.deleting') : t('formBuilder.yesDeleteIt')}
              </Button>
              <Button size="desktop" variant="link" onClick={() => setConfirmingDelete(false)}>
                {t('common.cancel')}
              </Button>
            </div>
          </div>
        )}
      </div>

      {/* Access: the existing role/user editor, in a dialog. */}
      <Sheet open={accessOpen} onClose={() => setAccessOpen(false)} title={t('formBuilder.access')} desktopWidth={560}>
        <div className="flex flex-col gap-4 pb-2">
          <p className="text-[14px] text-ink-soft">{form.allowedRoleIds.length === 0 && form.allowedUserIds.length === 0 ? t('formBuilder.accessOpenDescription') : t('formBuilder.accessRestrictedDescription')}</p>
          <fieldset className="flex flex-col gap-2">
            <legend className="pb-2 text-[14px] font-bold text-ink">{t('formBuilder.byRole')}</legend>
            {roles.length === 0 ? (
              <span className="text-[14px] text-ink-soft">{t('formBuilder.noRolesYet')}</span>
            ) : (
              roles.map((r) => (
                <label key={r.id} className="flex min-h-[40px] items-center gap-2 text-[15px] text-ink">
                  <input type="checkbox" checked={pendingRoleIds.has(r.id)} onChange={() => setPendingRoleIds((p) => toggle(p, r.id))} />
                  {t(`roleName.${r.name}`, { defaultValue: r.name })}
                </label>
              ))
            )}
          </fieldset>
          <fieldset className="flex flex-col gap-2">
            <legend className="pb-2 text-[14px] font-bold text-ink">{t('formBuilder.byUser')}</legend>
            {users.length === 0 ? (
              <span className="text-[14px] text-ink-soft">{t('formBuilder.noUsersYet')}</span>
            ) : (
              users.map((u) => (
                <label key={u.id} className="flex min-h-[40px] items-center gap-2 text-[15px] text-ink">
                  <input type="checkbox" checked={pendingUserIds.has(u.id)} onChange={() => setPendingUserIds((p) => toggle(p, u.id))} />
                  {u.displayName}
                </label>
              ))
            )}
          </fieldset>
          <div>
            <Button size="desktop" disabled={busy || !accessChanged} onClick={saveAccess}>
              {busy ? t('formBuilder.savingAccess') : t('formBuilder.saveAccess')}
            </Button>
          </div>
        </div>
      </Sheet>

      {/* The form's name in both languages. */}
      <Sheet open={nameOpen} onClose={() => setNameOpen(false)} title={t('builderV2.editName')} desktopWidth={520}>
        <div className="flex flex-col gap-3 pb-2">
          <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
            {t('builderV2.nameAr')}
            <input dir="rtl" className={builderInput} value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
            {t('builderV2.nameEn')}
            <input dir="ltr" className={builderInput} value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </label>
          <div>
            <Button size="desktop" disabled={busy || !nameAr.trim()} onClick={saveName}>
              {t('common.save')}
            </Button>
          </div>
        </div>
      </Sheet>

      {/* "+ Add field or step": choose, then the existing add-field form or the step titles. */}
      <Sheet open={addOpen === 'choose'} onClose={() => setAddOpen('none')} title={t('builderV2.addWhich')} desktopWidth={420}>
        <div className="flex flex-col gap-2 pb-2">
          <Button size="desktop" variant="secondary" block onClick={() => setAddOpen('field')}>
            {t('formBuilder.addAField')}
          </Button>
          <Button
            size="desktop"
            variant="secondary"
            block
            onClick={() => {
              setAddOpen('none');
              setStepDialog({ id: null, titleAr: '', titleEn: '' });
            }}
          >
            {t('builderV2.addStep')}
          </Button>
          <Button
            size="desktop"
            variant="secondary"
            block
            onClick={() => {
              setAddOpen('none');
              setSectionDialog({ id: null, code: '', titleAr: '', titleEn: '', min: '1', max: '10' });
            }}
          >
            {t('sectionsBuilder.add')}
          </Button>
          {(version?.sections ?? []).map((sec) => (
            <Button
              key={sec.id}
              size="desktop"
              variant="link"
              block
              onClick={() => {
                setAddOpen('none');
                setSectionDialog({ id: sec.id, code: sec.code, titleAr: sec.titleAr, titleEn: sec.titleEn ?? '', min: String(sec.minBlocks), max: String(sec.maxBlocks) });
              }}
            >
              {t('sectionsBuilder.edit', { title: sec.titleAr })}
            </Button>
          ))}
          {/* System: calculations read it, so its fields can't be renamed or removed. Hidden: out of every menu. */}
          <label className="mt-2 flex min-h-[44px] items-center gap-2 text-[14px] text-ink">
            <input type="checkbox" checked={!!form.isSystem} disabled={busy} onChange={(e) => void act(() => api.forms.setFlags(token, formId, e.target.checked, !!form.isHidden), 'formBuilder.loadError')} />
            {t('formBuilder.systemForm')}
          </label>
          <label className="flex min-h-[44px] items-center gap-2 text-[14px] text-ink">
            <input type="checkbox" checked={!!form.isHidden} disabled={busy} onChange={(e) => void act(() => api.forms.setFlags(token, formId, !!form.isSystem, e.target.checked), 'formBuilder.loadError')} />
            {t('formBuilder.hiddenForm')}
          </label>
        </div>
      </Sheet>

      <Sheet open={sectionDialog !== null} onClose={() => setSectionDialog(null)} title={sectionDialog?.id ? t('sectionsBuilder.editTitle') : t('sectionsBuilder.add')} desktopWidth={520}>
        {sectionDialog && (
          <div className="flex flex-col gap-3 pb-2">
            <p className="text-[14px] text-ink-soft">{t('sectionsBuilder.explain')}</p>
            {!sectionDialog.id && (
              <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
                {t('addField.code')}
                <input dir="ltr" className={builderInput} value={sectionDialog.code} onChange={(e) => setSectionDialog({ ...sectionDialog, code: e.target.value })} />
              </label>
            )}
            <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
              {t('builderV2.stepTitleAr')}
              <input dir="rtl" className={builderInput} value={sectionDialog.titleAr} onChange={(e) => setSectionDialog({ ...sectionDialog, titleAr: e.target.value })} />
            </label>
            <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
              {t('builderV2.stepTitleEn')}
              <input dir="ltr" className={builderInput} value={sectionDialog.titleEn} onChange={(e) => setSectionDialog({ ...sectionDialog, titleEn: e.target.value })} />
            </label>
            <div className="grid grid-cols-2 gap-3">
              <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
                {t('sectionsBuilder.min')}
                <input inputMode="numeric" dir="ltr" className={builderInput} value={sectionDialog.min} onChange={(e) => setSectionDialog({ ...sectionDialog, min: e.target.value })} />
              </label>
              <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
                {t('sectionsBuilder.max')}
                <input inputMode="numeric" dir="ltr" className={builderInput} value={sectionDialog.max} onChange={(e) => setSectionDialog({ ...sectionDialog, max: e.target.value })} />
              </label>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button size="desktop" disabled={busy || !sectionDialog.titleAr.trim() || (!sectionDialog.id && !sectionDialog.code.trim())} onClick={saveSection}>
                {t('common.save')}
              </Button>
              {sectionDialog.id && (
                <Button size="desktop" variant="danger" disabled={busy} onClick={removeSection}>
                  {t('sectionsBuilder.remove')}
                </Button>
              )}
            </div>
          </div>
        )}
      </Sheet>
      <Sheet open={addOpen === 'field'} onClose={() => setAddOpen('none')} title={t('formBuilder.addAField')} desktopWidth={680}>
        <div className="pb-2">
          <AddFieldForm
            token={token}
            formId={formId}
            lookupTargets={lookupTargets}
            controllableFields={activeFields.filter((f) => f.fieldType === 'Dropdown' || f.fieldType === 'Boolean')}
            filterableFields={activeFields}
            steps={steps.filter((s) => s.id).map((s) => ({ id: s.id!, title: s.title }))}
            defaultStepId={selectedStepIndex >= 0 ? steps[selectedStepIndex].id : null}
            sections={(version?.sections ?? []).map((sec) => ({ id: sec.id, title: sec.titleAr }))}
            onAdded={() => {
              setAddOpen('none');
              void load();
            }}
          />
        </div>
      </Sheet>

      <Sheet open={stepDialog !== null} onClose={() => setStepDialog(null)} title={stepDialog?.id ? t('builderV2.renameStep') : t('builderV2.addStep')} desktopWidth={520}>
        {stepDialog && (
          <div className="flex flex-col gap-3 pb-2">
            <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
              {t('builderV2.stepTitleAr')}
              <input dir="rtl" className={builderInput} value={stepDialog.titleAr} onChange={(e) => setStepDialog({ ...stepDialog, titleAr: e.target.value })} />
            </label>
            <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
              {t('builderV2.stepTitleEn')}
              <input dir="ltr" className={builderInput} value={stepDialog.titleEn} onChange={(e) => setStepDialog({ ...stepDialog, titleEn: e.target.value })} />
            </label>
            <div className="flex flex-wrap gap-2">
              <Button size="desktop" disabled={busy || !stepDialog.titleAr.trim()} onClick={saveStep}>
                {t('common.save')}
              </Button>
              {stepDialog.id && (
                <Button size="desktop" variant="danger" disabled={busy} onClick={deleteStep}>
                  {t('builderV2.deleteStep')}
                </Button>
              )}
            </div>
          </div>
        )}
      </Sheet>
    </div>
  );
}

function toggle(set: Set<string>, id: string) {
  const next = new Set(set);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  return next;
}
