import { useEffect, useMemo, useRef, useState } from 'react';
import type { AnimationEvent, ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import i18n from '../i18n';
import { api, ApiError } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { SentScreen } from './SentScreen';
import { todayIso } from './DateField';
import { FieldControl } from './form/FieldControl';
import { StepFooter, StepHeader } from './form/StepChrome';
import { Icon } from './icons';
import { FULL_SCREEN_ATTRIBUTE } from './shell/fullScreen';
import { useOptionalShell } from './shell/ShellData';
import { Button, useIsDesktop } from './ui';
import { lastChoice, recentChoices, rememberChoice } from '../forms/lookupMemory';
import { checkValue, isFieldVisible as fieldIsVisible, parseOptions, recordToFormValues } from '../forms/fieldValues';
import type { FieldProblem } from '../forms/fieldValues';
import type { FieldDefinitionDto, FormDefinitionDto } from '../types';

interface FormRendererProps {
  token: string;
  formDefinition: FormDefinitionDto;
  onSubmitted?: () => void;
  /** True while the Sent screen (07) replaces the form, so the page can hide its own title. */
  onSentChange?: (shown: boolean) => void;
  /** Set to correct an existing record (administrators): the form opens with its values and
   *  saving replaces them. Attachments aren't edited here - they have their own panel. */
  editRecord?: { id: string; values: Record<string, unknown> };
  onCancelEdit?: () => void;
}

interface LookupChoice {
  id: string;
  label: string;
}

// A per-field problem is either one this component found itself (an i18n key, so it
// re-translates live on a language switch) or the backend's own message, already resolved to
// English text by SubmissionValueValidator and shown as-is - the checks below mirror the
// backend's, so that only happens for a submission that got past them somehow.
type FieldError = FieldProblem | { text: string };
const REQUIRED: FieldError = { key: 'formRenderer.fieldRequired' };

// Arabic keyboards type Arabic-Indic digits (٠١٢…) and the Arabic decimal mark; the number
// fields accept them and store plain digits.
function toLatinDigits(text: string): string {
  return text
    .replace(/[\u0660-\u0669]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/[\u06F0-\u06F9]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/[\u066B\u060C]/g, '.');
}

/**
 * Renders a submission form for ANY published form, driven entirely by its field
 * metadata - this is the actual "low-code" part of the platform. No per-form code exists
 * or should ever need to exist here.
 *
 * Attachments are picked here, inline with everything else, but physically uploaded in a
 * second step immediately after the record is created - a record's Id has to exist before
 * a file can be associated with it (see FileMetadata.RecordId). That ordering is invisible
 * to the person filling the form: one "Submit" click does both, in sequence. If the record
 * saves but a file fails to upload, that's surfaced honestly as a partial success, not a
 * generic failure - the data isn't lost, just that one file needs retrying afterward via
 * the Attachments panel.
 *
 * Lookup choices come from the backend's lookup-options endpoint (id + label only), checked
 * against access to THIS form - so someone who may fill in a form but not open the master-
 * data form it points at (a project manager and the Projects form) still gets the choices.
 * The label is the target form's first ShortText field; there is still no designated
 * "display field" on FormDefinition (see CLAUDE.md's known gaps).
 */
export function FormRenderer({ token, formDefinition, onSubmitted, onSentChange, editRecord, onCancelEdit }: FormRendererProps) {
  const { t } = useTranslation();
  const isEditing = !!editRecord;
  const activeFields = useMemo(
    () =>
      formDefinition.publishedVersion?.fields.filter((f) => f.isActive && !(isEditing && f.fieldType === 'Attachment')) ?? [],
    [formDefinition, isEditing],
  );

  // A required date starts on today: most entries (a daily report, a stock movement) are for
  // today, and it is one less thing to fill in. Optional dates (e.g. a project's expected
  // completion) start empty, since today would usually be wrong for them. A record being
  // edited starts from its own stored values instead.
  const initialValues = (): Record<string, string> =>
    editRecord
      ? recordToFormValues(activeFields, editRecord.values)
      : Object.fromEntries(activeFields.filter((f) => f.fieldType === 'DateTime' && f.isRequired).map((f) => [f.code, todayIso()]));

  const [values, setValues] = useState<Record<string, string>>(initialValues);
  // Set when a submission has just been saved: the form is replaced by a confirmation until
  // the person chooses to submit another, so success is impossible to miss.
  const [submittedAt, setSubmittedAt] = useState<Date | null>(null);
  const [sentSummary, setSentSummary] = useState<{ project: { label: string; value: string } | null; attachments: number }>({ project: null, attachments: 0 });
  const [attachmentFiles, setAttachmentFiles] = useState<Record<string, File[]>>({});
  // Errors show on Send, then for each field again when the person leaves it (HANDOFF §6:
  // never while typing the first time).
  const [attempted, setAttempted] = useState(false);
  // Lookups pre-filled from the person's last choice, shown as "Your last choice".
  const [preselected, setPreselected] = useState<Record<string, string>>({});
  const shell = useOptionalShell();
  const userId = shell?.me?.id ?? null;
  const desktop = useIsDesktop();
  const navigate = useNavigate();
  const [lookupChoices, setLookupChoices] = useState<Record<string, LookupChoice[]>>({});
  const [dynamicDropdownOptions, setDynamicDropdownOptions] = useState<Record<string, string[]>>({});
  const [submitting, setSubmitting] = useState(false);
  useEffect(() => {
    onSentChange?.(submittedAt !== null);
  }, [submittedAt, onSentChange]);
  const [error, setError] = useErrorMessage();
  const [fieldErrors, setFieldErrors] = useState<Record<string, FieldError>>({});

  useEffect(() => {
    setValues(initialValues());
    setAttachmentFiles({});
    setError(null);
    setFieldErrors({});
    setSubmittedAt(null);

    // Dynamic-options Dropdowns (e.g. Outflow's "material" sourced from Inflow's "material")
    // fetch once per form load, same cadence as an unfiltered Lookup - their live distinct-
    // values set doesn't depend on anything else on THIS form the way a filtered Lookup's does.
    const dynamicDropdownFields = activeFields.filter(
      (f) => f.fieldType === 'Dropdown' && f.dynamicOptionsSourceFormDefinitionId && f.dynamicOptionsSourceFieldCode,
    );
    dynamicDropdownFields.forEach(async (field) => {
      try {
        const opts = await api.forms.getFieldDynamicOptions(token, formDefinition.id, field.id);
        setDynamicDropdownOptions((prev) => ({ ...prev, [field.code]: opts }));
      } catch {
        // Same philosophy as the Lookup fetches below - a failed fetch just leaves this
        // field without options rather than blocking the rest of the form.
      }
    });

    // Filtered (cascading) Lookups are excluded here - they have no single fixed choice list
    // to fetch once at load, since their candidates depend on another field's current value.
    // The effect below handles those, reactively, keyed off that value instead of form load.
    const lookupFields = activeFields.filter(
      (f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId && !f.filterByFieldCode,
    );
    lookupFields.forEach(async (field) => {
      try {
        const choices: LookupChoice[] = await api.forms.lookupOptions(token, formDefinition.id, field.id);
        setLookupChoices((prev) => ({ ...prev, [field.code]: choices }));
      } catch {
        // A failed lookup fetch shouldn't block the rest of the form from rendering -
        // that field just won't have options, and the required-field check below will
        // catch it if the user tries to submit without picking one.
      }
    });
    // activeFields is derived from formDefinition each render via useMemo, not a stable
    // reference - depend on the form's id/version instead to avoid re-fetching on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formDefinition.id, formDefinition.publishedVersion?.id, token, editRecord?.id]);

  const filteredLookupFields = useMemo(
    () => activeFields.filter((f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId && f.filterByFieldCode),
    [activeFields],
  );

  // Keyed by a plain string, not the fields/values objects themselves, so this only re-runs
  // when a FILTER SOURCE field's value actually changes - not on every keystroke in an
  // unrelated field on the form.
  const filterSourceValuesKey = filteredLookupFields
    .map((f) => `${f.code}=${values[f.filterByFieldCode as string] ?? ''}`)
    .join('&');

  // Re-fetches each filtered (cascading) Lookup's choices whenever ITS OWN filter-source
  // field's current value changes - e.g. "zone" filtered by "project" re-fetches the moment
  // a project is picked or changed, so it only ever offers zones belonging to whichever
  // project is currently selected, never every zone from every project.
  useEffect(() => {
    filteredLookupFields.forEach(async (field) => {
      const sourceValue = values[field.filterByFieldCode as string];
      if (!sourceValue) {
        // No filter value chosen yet (e.g. no project picked) - show nothing rather than
        // every row from every project, which is exactly what this feature exists to avoid.
        setLookupChoices((prev) => ({ ...prev, [field.code]: [] }));
        return;
      }
      try {
        const choices: LookupChoice[] = await api.forms.lookupOptions(token, formDefinition.id, field.id, sourceValue);
        setLookupChoices((prev) => ({ ...prev, [field.code]: choices }));
      } catch {
        // Same philosophy as the unfiltered fetch above - a failed fetch just leaves this
        // field without options rather than blocking the rest of the form.
      }
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterSourceValuesKey, token]);

  function setValue(code: string, value: string) {
    setValues((prev) => ({ ...prev, [code]: value }));
    // The problem shown under a field goes away as soon as it is being fixed.
    setFieldErrors((prev) => {
      if (!(code in prev)) return prev;
      const next = { ...prev };
      delete next[code];
      return next;
    });
  }

  function setFieldFiles(code: string, files: File[]) {
    setAttachmentFiles((prev) => ({ ...prev, [code]: files }));
    setFieldErrors((prev) => {
      if (!(code in prev)) return prev;
      const next = { ...prev };
      delete next[code];
      return next;
    });
  }

  // A Lookup with a remembered last choice starts on it (HANDOFF §9.7); not when editing a record.
  useEffect(() => {
    if (editRecord || !userId) return;
    const updates: Record<string, string> = {};
    for (const f of activeFields) {
      if (f.fieldType !== 'Lookup' || values[f.code] || preselected[f.code] !== undefined) continue;
      const last = lastChoice(userId, f.id);
      if (last && lookupChoices[f.code]?.some((c) => c.id === last)) updates[f.code] = last;
    }
    if (Object.keys(updates).length === 0) return;
    setPreselected((p) => ({ ...p, ...updates }));
    setValues((v) => ({ ...updates, ...v }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lookupChoices, userId]);

  /** The problem with one field's current answer, or null. Same rules as on Send. */
  function problemFor(f: FieldDefinitionDto): FieldError | null {
    const isMissing = f.fieldType === 'Attachment' ? !(attachmentFiles[f.code]?.length) : !(values[f.code] ?? '').trim();
    if (isMissing) return f.isRequired ? REQUIRED : null;
    if (f.fieldType === 'Attachment') return null;
    return checkValue(f, values[f.code]) ?? null;
  }

  function onFieldBlur(f: FieldDefinitionDto) {
    if (!attempted) return;
    const problem = problemFor(f);
    setFieldErrors((prev) => {
      const next = { ...prev };
      if (problem) next[f.code] = problem;
      else delete next[f.code];
      return next;
    });
  }

  // Same rule as the backend's SubmissionValueValidator.IsFieldVisible (both read the
  // allowed values the same tolerant way - see forms/fieldValues.ts). This is what shows the
  // right block as the person picks a Phase; the backend copy still enforces it if a
  // submission ever bypasses this UI.
  const isFieldVisible = (field: FieldDefinitionDto) => fieldIsVisible(field, values);

  const visibleFields = useMemo(() => activeFields.filter(isFieldVisible), [activeFields, values]);

  // When a controlling field's value changes and that hides a field that previously had a
  // value, drop the stale value from state too - otherwise switching Phase from Excavation to
  // Foundation and back would silently resubmit a leftover zone value alongside the new
  // footing one, even though the zone input is no longer shown. Comparing against
  // visibleFields (not activeFields) is what limits this to fields that just became hidden.
  useEffect(() => {
    const hiddenCodesWithValues = activeFields
      .filter((f) => !isFieldVisible(f) && values[f.code] !== undefined)
      .map((f) => f.code);
    if (hiddenCodesWithValues.length === 0) return;
    setValues((prev) => {
      const next = { ...prev };
      hiddenCodesWithValues.forEach((code) => delete next[code]);
      return next;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [values]);

  // Same idea, for a filtered Lookup whose filter SOURCE field just changed value: its
  // previously-picked value almost certainly isn't one of the new filtered candidates (e.g.
  // a zone that belonged to the OLD project), so it's cleared too - rather than left silently
  // referencing a row the new filter wouldn't have offered. Deliberately keyed only on
  // filterSourceValuesKey (not `values`), with the mount-skip below, so this never fires just
  // because the user picked a zone itself (which also changes `values`, but isn't a source
  // change) - only an actual change in a SOURCE field's value should clear its dependents.
  const previousFilterSourceValuesKey = useRef(filterSourceValuesKey);
  useEffect(() => {
    if (previousFilterSourceValuesKey.current === filterSourceValuesKey) return;
    previousFilterSourceValuesKey.current = filterSourceValuesKey;

    const staleCodes = filteredLookupFields.filter((f) => values[f.code] !== undefined).map((f) => f.code);
    if (staleCodes.length === 0) return;
    setValues((prev) => {
      const next = { ...prev };
      staleCodes.forEach((code) => delete next[code]);
      return next;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterSourceValuesKey]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    setAttempted(true);
    const problems: Record<string, FieldError> = {};
    visibleFields.forEach((f) => {
      // Blank or spaces-only counts as not answered, as it does on the server.
      const problem = problemFor(f);
      if (problem) problems[f.code] = problem;
    });
    if (Object.keys(problems).length > 0) {
      setFieldErrors(problems);
      // Bring the first problem into view - on a phone it is usually far above the button.
      const first = visibleFields.find((f) => problems[f.code]);
      if (first) {
        const box = document.getElementById(`field-${first.id}`);
        box?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        box?.querySelector<HTMLElement>('input, textarea, button, [role=radio]')?.focus({ preventScroll: true });
      }
      return;
    }
    setFieldErrors({});

    setSubmitting(true);
    try {
      const payload: Record<string, unknown> = {};
      visibleFields.forEach((f) => {
        if (f.fieldType === 'Attachment') return; // never part of the JSON body - no physical column
        const raw = values[f.code]?.trim();
        if (raw === undefined || raw === '') {
          payload[f.code] = null;
        } else if (f.fieldType === 'Number' || f.fieldType === 'Decimal') {
          payload[f.code] = Number(raw);
        } else if (f.fieldType === 'Boolean') {
          payload[f.code] = raw === 'true';
        } else {
          payload[f.code] = raw;
        }
      });

      if (editRecord) {
        await api.submissions.update(token, formDefinition.id, editRecord.id, payload);
        onSubmitted?.();
        return;
      }

      const { id: recordId } = await api.submissions.submit(token, formDefinition.id, payload);

      const uploads = visibleFields
        .filter((f) => f.fieldType === 'Attachment')
        .flatMap((f) => (attachmentFiles[f.code] ?? []).map((file) => ({ field: f, file })));

      if (uploads.length > 0) {
        const results = await Promise.allSettled(
          uploads.map((u) => api.files.upload(token, formDefinition.id, recordId, u.field.code, u.file)),
        );
        const failedLabels = results
          .map((r, i) => (r.status === 'rejected' ? uploads[i].file.name : null))
          .filter((label): label is string => label !== null);

        if (failedLabels.length > 0) {
          // The record itself saved fine - only the file(s) failed. Say so plainly rather
          // than implying the whole submission failed, since the data wasn't lost.
          setError({
            key: 'formRenderer.partialUploadFailure',
            params: { count: failedLabels.length, files: failedLabels.join(', ') },
          });
        }
      }

      // The Sent screen's summary (screen 07): the project, if the form has a project field,
      // and how many files were attached.
      const projectField = visibleFields.find((f) => f.fieldType === 'Lookup' && f.code === 'project');
      const projectChoice = projectField ? lookupChoices[projectField.code]?.find((c) => c.id === values[projectField.code]) : undefined;
      setSentSummary({
        project: projectField && projectChoice ? { label: projectField.label, value: projectChoice.label } : null,
        attachments: uploads.length,
      });
      // Remember the choices made, for "Recent" and "Your last choice" next time.
      visibleFields
        .filter((f) => f.fieldType === 'Lookup' || f.fieldType === 'Dropdown')
        .forEach((f) => values[f.code] && rememberChoice(userId, f.id, values[f.code]));
      setAttempted(false);
      setPreselected({});
      setValues(initialValues());
      setAttachmentFiles({});
      setSubmittedAt(new Date());
      onSubmitted?.();
    } catch (err) {
      if (err instanceof ApiError && err.errors) {
        // Backend keys these by field.code exactly (see SubmissionValueValidator). The checks
        // that read other records (SubmissionDataChecks) also send a code with a translation;
        // otherwise the first message per field is shown as-is.
        const translated = err.code && i18n.exists(`apiErrors.${err.code}`) ? `apiErrors.${err.code}` : null;
        const perField: Record<string, FieldError> = {};
        Object.entries(err.errors).forEach(([code, messages]) => {
          if (messages.length > 0) perField[code] = translated ? { key: translated } : { text: messages[0] };
        });
        setFieldErrors(perField);
        setError(null);
      } else {
        setError({ err, fallbackKey: 'formRenderer.submitFailed' });
      }
    } finally {
      setSubmitting(false);
    }
  }

  if (!formDefinition.publishedVersion) {
    return <p className="text-sm text-ink-soft">{t('formRenderer.noPublishedVersion')}</p>;
  }

  if (submittedAt) {
    return (
      <SentScreen
        formName={formDefinition.name}
        submittedAt={submittedAt}
        project={sentSummary.project}
        attachments={sentSummary.attachments}
        warning={error}
        onAnother={() => {
          setSubmittedAt(null);
          setError(null);
        }}
      />
    );
  }

  // ---------------------------------------------------------------- rendering (HANDOFF §6-§8)

  const errorText = (code: string) => {
    const e = fieldErrors[code];
    return e ? ('text' in e ? e.text : t(e.key, e.params)) : null;
  };
  const fieldsById = new Map(activeFields.map((f) => [f.code, f]));

  const control = (field: FieldDefinitionDto, inCard = false) => (
    <div key={field.id} id={`field-${field.id}`} onBlur={() => onFieldBlur(field)}>
      <FieldControl
        field={field}
        value={values[field.code] ?? ''}
        onChange={(v) => {
          setValue(field.code, field.fieldType === 'Number' || field.fieldType === 'Decimal' ? toLatinDigits(v) : v);
          if (preselected[field.code] !== undefined && v !== preselected[field.code]) setPreselected((p) => ({ ...p, [field.code]: '' }));
        }}
        files={attachmentFiles[field.code] ?? []}
        onFiles={(files) => setFieldFiles(field.code, files)}
        error={errorText(field.code)}
        options={parseOptions(field.optionsJson)}
        dynamicOptions={dynamicDropdownOptions[field.code]}
        lookupChoices={lookupChoices[field.code]}
        filterWaitingOnLabel={
          field.filterByFieldCode && !values[field.filterByFieldCode]
            ? fieldsById.get(field.filterByFieldCode)?.label ?? field.filterByFieldCode
            : undefined
        }
        isFiltered={!!field.filterByFieldCode}
        filteredByValue={
          field.filterByFieldCode ? lookupChoices[field.filterByFieldCode]?.find((c) => c.id === values[field.filterByFieldCode!])?.label : undefined
        }
        recent={recentChoices(userId, field.id)}
        isLastChoice={!!preselected[field.code] && preselected[field.code] === values[field.code]}
        inCard={inCard}
      />
    </div>
  );

  // The reveal animates height, which needs the block clipped - but only while it runs, or a
  // focused field's ring inside it is cut off.
  const unclip = (e: AnimationEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) e.currentTarget.style.overflow = 'visible';
  };

  // Fields shown because of another answer sit right under it (05): under a yes/no, indented
  // with a 3px accent bar; under a type/phase selector, in a card headed "Shown because you
  // chose: ...". Consecutive dependents of the same selector share one card.
  const blocks: ReactNode[] = [];
  for (let i = 0; i < visibleFields.length; i++) {
    const field = visibleFields[i];
    const controller = field.visibleWhenFieldCode ? fieldsById.get(field.visibleWhenFieldCode) : undefined;
    if (!controller) {
      blocks.push(control(field));
      continue;
    }
    if (controller.fieldType === 'Dropdown') {
      const group = [field];
      while (i + 1 < visibleFields.length && visibleFields[i + 1].visibleWhenFieldCode === controller.code) group.push(visibleFields[++i]);
      const chosen = parseOptions(controller.optionsJson).find((o) => o.value === values[controller.code])?.label ?? values[controller.code];
      blocks.push(
        <div key={`cond-${field.id}`} onAnimationEnd={unclip} className="flex animate-[reveal_180ms_ease-out] flex-col gap-2 overflow-hidden rounded-ui-2xl border border-border bg-panel p-[14px]">
          <span className="self-start rounded-ui-pill bg-accent-soft px-[10px] py-1 text-[12px] font-bold text-accent-strong">
            {t('steps.shownBecause', { value: chosen })}
          </span>
          <div className="flex flex-col gap-[22px]">{group.map((f) => control(f, true))}</div>
        </div>,
      );
    } else {
      blocks.push(
        <div key={`cond-${field.id}`} onAnimationEnd={unclip} className="-mt-2 animate-[reveal_180ms_ease-out] overflow-hidden border-s-[3px] border-accent ps-3">
          {control(field)}
        </div>,
      );
    }
  }

  const sendLabel = isEditing
    ? submitting
      ? t('formRenderer.saving')
      : t('formRenderer.saveChanges')
    : submitting
      ? t('steps.sending')
      : t('steps.send');

  const messages = (
    <>
      {isEditing && formDefinition.publishedVersion?.fields.some((f) => f.isActive && f.fieldType === 'Attachment') && (
        <p className="text-[13px] text-ink-soft">{t('formRenderer.attachmentsNotEditedHere')}</p>
      )}
      {Object.keys(fieldErrors).length > 0 && (
        <p className="flex items-center gap-1.5 text-[14px] text-danger" role="alert">
          <Icon name="alert" size={16} />
          {t('formRenderer.fixHighlighted')}
        </p>
      )}
      {error && <p className="text-[14px] text-danger">{error}</p>}
    </>
  );

  const sendButton = (size: 'phone' | 'desktop') => (
    <Button
      type="submit"
      variant={isEditing ? 'primary' : 'success'}
      size={size}
      block
      disabled={submitting}
      icon={isEditing ? undefined : <Icon name="send" size={20} />}
    >
      {sendLabel}
    </Button>
  );
  const cancelButton = (size: 'phone' | 'desktop') =>
    isEditing && onCancelEdit ? (
      <Button variant="secondary" size={size} onClick={onCancelEdit} disabled={submitting} className="w-[96px] shrink-0">
        {t('common.cancel')}
      </Button>
    ) : null;

  // Until steps exist (phase 6), every form is one step titled with the form's name.
  const stepTitle = formDefinition.name;

  if (desktop || isEditing) {
    // Desktop Fill (screen 20, "Fill" tab): one column, one card per step with the step title
    // as its heading, and a sticky footer holding Send. Also used to correct a record.
    return (
      <form onSubmit={handleSubmit} noValidate className="flex max-w-[720px] flex-col gap-4">
        <section className="flex flex-col gap-[22px] rounded-ui-2xl border border-border bg-panel p-5">
          <h2 className="text-[18px] font-bold text-ink">
            <bdi>{stepTitle}</bdi>
          </h2>
          {blocks}
        </section>
        {messages}
        <div className="sticky bottom-0 z-10 -mx-1 flex gap-2 bg-bg px-1 py-3">
          {cancelButton('desktop')}
          {sendButton('desktop')}
        </div>
      </form>
    );
  }

  // Phone (03-06, 11): the form takes the whole screen - a step header, the fields, and a
  // footer with Send that stays at the bottom. No tab bar while filling.
  return (
    <FullScreen>
      <form onSubmit={handleSubmit} noValidate className="-mx-5 -mt-5 flex min-h-[100dvh] flex-col bg-bg">
        <StepHeader
          title={formDefinition.name}
          step={1}
          total={1}
          stepTitle={stepTitle}
          onClose={() => (window.history.length > 1 ? navigate(-1) : navigate('/'))}
        />
        <div className="flex flex-1 flex-col gap-[22px] p-5">
          {blocks}
          {messages}
        </div>
        <StepFooter>{sendButton('phone')}</StepFooter>
      </form>
    </FullScreen>
  );
}

/** Hides the phone tab bar while mounted (filling a form brings its own bars). */
function FullScreen({ children }: { children: ReactNode }) {
  useEffect(() => {
    document.documentElement.setAttribute(FULL_SCREEN_ATTRIBUTE, '');
    return () => document.documentElement.removeAttribute(FULL_SCREEN_ATTRIBUTE);
  }, []);
  return <>{children}</>;
}
