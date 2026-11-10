import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { todayIso } from '../DateField';
import { api } from '../../api/client';
import { useErrorMessage } from '../../hooks/useErrorMessage';
import { parseOptions, storedToSaudiDate } from '../../forms/fieldValues';
import { useLocalized } from '../../forms/localized';
import { RecordBlocks } from './RecordBlocks';
import { answerText, BLOCK_PLAN_FIELD, dayLabel, DELAY_FIELD, localeFor, PLAN_FIELD, sentDay, sentTime, shownFields, shownSections } from '../../forms/recordAnswers';
import { pickLanguage } from '../../forms/localized';
import { Thumbnail } from '../../routes/EntryDetail';
import type { FieldDefinitionDto, FormDefinitionDto, RecordDetailDto, RecordItemDto } from '../../types';
import { Icon } from '../icons';
import { useOptionalShell } from '../shell/ShellData';
import { Button, ClickableRow, Drawer, EmptyState, ErrorState, LoadingState, Pager, TableCard, tdClass, thClass } from '../ui';
import { WorkflowPanel } from '../WorkflowPanel';

const PAGE_SIZE = 25;
const DEFAULT_COLUMNS = 9;
const chip = (on: boolean) =>
  `flex h-[40px] items-center gap-1.5 whitespace-nowrap rounded-ui-pill border px-[14px] text-[14px] ${on ? 'border-accent bg-accent-soft font-bold text-accent-strong' : 'border-border bg-panel text-ink'}`;

function isoDay(offsetDays: number) {
  return new Date(Date.now() + 3 * 3600_000 + offsetDays * 86_400_000).toISOString().slice(0, 10);
}

/**
 * The Records tab (screen 20, HANDOFF §9.4): search over the text answers, Project, a date range
 * (default "Last 30 days", removable), Phase and "With a delay", all filtered on the server; a
 * "Columns (shown of total)" menu (default: the first 9 non-long-text fields, kept per person per
 * form); a table - date bold first, chosen columns, "Sent by" last - with a pager; and a record
 * drawer with every shown answer, its attachments (+ Add), the workflow status when the form
 * has a workflow, and Edit / Delete (the existing correction and soft-delete flows).
 */
export function RecordsTab({
  token,
  form,
  refreshKey,
  onEdit,
  onCount,
}: {
  token: string;
  form: FormDefinitionDto;
  /** Bump to reload (after an edit). */
  refreshKey: number;
  onEdit: (record: RecordItemDto) => void;
  onCount: (count: number) => void;
}) {
  const { t, i18n } = useTranslation();
  const { fieldLabel, optionLabel } = useLocalized();
  const userId = useOptionalShell()?.me?.id ?? 'anon';
  const locale = localeFor(i18n.language);
  const ctx = useMemo(() => ({ t, lang: i18n.language, locale }), [t, i18n.language, locale]);

  // Header fields only: a repeatable section's fields live in its blocks (own column below).
  const fields = useMemo(() => [...(form.publishedVersion?.fields ?? [])].filter((f) => f.isActive && !f.sectionId).sort((a, b) => a.displayOrder - b.displayOrder), [form]);
  const sections = form.publishedVersion?.sections ?? [];
  // The filter row's fields: the first Lookup (the record's project), the first dropdown other
  // fields depend on (the phase - or simply the first dropdown), the delay question, the first date.
  const projectField = fields.find((f) => f.fieldType === 'Lookup' && !f.visibleWhenFieldCode);
  const controllers = new Set(fields.map((f) => f.visibleWhenFieldCode));
  const phaseField = fields.find((f) => f.fieldType === 'Dropdown' && controllers.has(f.code) && f.optionsJson) ?? fields.find((f) => f.fieldType === 'Dropdown' && f.optionsJson && !f.visibleWhenFieldCode);
  const delayField = fields.find((f) => f.code === DELAY_FIELD && f.fieldType === 'Boolean');
  const dateField = fields.find((f) => f.fieldType === 'DateTime');
  const columnChoices = fields.filter((f) => f.fieldType !== 'LongText' && f.fieldType !== 'Attachment' && f.id !== dateField?.id);

  const storageKey = `records-columns:${userId}:${form.id}`;
  const [columns, setColumns] = useState<string[]>(() => {
    try {
      const saved = JSON.parse(localStorage.getItem(storageKey) ?? 'null');
      if (Array.isArray(saved)) return saved.filter((c): c is string => typeof c === 'string');
    } catch {
      // first visit, or storage blocked
    }
    return columnChoices.slice(0, DEFAULT_COLUMNS).map((f) => f.code);
  });
  useEffect(() => {
    try {
      localStorage.setItem(storageKey, JSON.stringify(columns));
    } catch {
      // not remembered - no harm
    }
  }, [columns, storageKey]);

  const [q, setQ] = useState('');
  const [query, setQuery] = useState('');
  const [project, setProject] = useState('');
  const [phase, setPhase] = useState('');
  const [delay, setDelay] = useState(false);
  const [last30, setLast30] = useState(true);
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<{ items: RecordItemDto[]; total: number } | null>(null);
  const [retry, setRetry] = useState(0);
  const [projects, setProjects] = useState<{ id: string; label: string }[]>([]);
  const [columnsOpen, setColumnsOpen] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useErrorMessage();

  // Typing searches after a short pause, not on every key.
  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(q), 300);
    return () => window.clearTimeout(timer);
  }, [q]);
  useEffect(() => setPage(1), [query, project, phase, delay, last30]);

  useEffect(() => {
    if (!projectField) return;
    api.forms.lookupOptions(token, form.id, projectField.id).then(setProjects).catch(() => setProjects([]));
  }, [token, form.id, projectField]);

  useEffect(() => {
    let alive = true;
    const filters: Record<string, string> = {};
    if (projectField && project) filters[projectField.code] = project;
    if (phaseField && phase) filters[phaseField.code] = phase;
    if (delayField && delay) filters[delayField.code] = 'true';
    api.submissions
      .search(token, form.id, {
        page,
        pageSize: PAGE_SIZE,
        q: query,
        filters,
        ...(last30 ? { from: isoDay(-29), to: isoDay(0), dateField: dateField?.code } : {}),
      })
      .then((r) => {
        if (!alive) return;
        setResult({ items: r.items, total: r.totalCount });
        setError(null);
      })
      .catch((err) => alive && setError({ err, fallbackKey: 'formView.loadError' }));
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, form.id, page, query, project, phase, delay, last30, refreshKey, retry]);

  // The tab's count is the whole form, not the filtered list.
  useEffect(() => {
    api.submissions
      .search(token, form.id, { page: 1, pageSize: 1 })
      .then((r) => onCount(r.totalCount))
      .catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, form.id, refreshKey]);

  const shownColumns = columnChoices.filter((f) => columns.includes(f.code));
  const cell = (f: FieldDefinitionDto, r: RecordItemDto) => {
    const v = r.values[f.code];
    if (f.code === PLAN_FIELD && typeof v === 'boolean')
      return <span className={`whitespace-nowrap rounded-ui-pill px-[9px] py-[3px] text-[12px] font-bold ${v ? 'bg-success-soft text-success' : 'bg-danger-soft text-danger'}`}>{v ? t('entries.tagPlanMet') : t('entries.tagPlanNotMet')}</span>;
    if (f.code === DELAY_FIELD) return <span className="font-bold text-warning">{v === true ? t('common.yes') : '—'}</span>;
    return <bdi>{answerText(f, r, ctx) ?? '—'}</bdi>;
  };
  const dateOf = (r: RecordItemDto) => shortDay(r, dateField, locale);

  const projectLabel = projects.find((p) => p.id === project)?.label;
  const phaseOptions = phaseField ? parseOptions(phaseField.optionsJson) : [];
  const total = result?.total ?? 0;
  const from = (page - 1) * PAGE_SIZE + 1;

  return (
    <div className="flex flex-col gap-4">
      {/* Filter row */}
      <div className="flex flex-wrap items-center gap-2">
        <label className="flex h-[40px] min-w-[220px] items-center gap-2 rounded-ui-lg border border-border bg-panel px-3">
          <Icon name="search" size={18} className="text-ink-soft" />
          <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t('records.search')} aria-label={t('records.search')} className="w-40 min-w-0 flex-1 bg-transparent text-[15px] text-ink placeholder:text-ink-soft focus:outline-none" />
        </label>
        {projectField && (
          <label className={`${chip(!!project)} relative`}>
            {t('records.filterProject', { value: projectLabel ?? t('records.filterAll') })}
            <Icon name="chevronDown" size={14} />
            <select aria-label={fieldLabel(projectField)} value={project} onChange={(e) => setProject(e.target.value)} className="absolute inset-0 cursor-pointer opacity-0">
              <option value="">{t('records.filterAll')}</option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.label}
                </option>
              ))}
            </select>
          </label>
        )}
        <button type="button" className={chip(last30)} aria-pressed={last30} onClick={() => setLast30(!last30)}>
          {last30 ? (
            <>
              {t('records.last30Days')} <Icon name="close" size={14} />
            </>
          ) : (
            t('records.dateRange')
          )}
        </button>
        {phaseField && (
          <label className={`${chip(!!phase)} relative`}>
            {t('records.filterPhase', { value: phase ? optionLabel(phaseOptions.find((o) => o.value === phase)!) : t('records.filterAll') })}
            <Icon name="chevronDown" size={14} />
            <select aria-label={fieldLabel(phaseField)} value={phase} onChange={(e) => setPhase(e.target.value)} className="absolute inset-0 cursor-pointer opacity-0">
              <option value="">{t('records.filterAll')}</option>
              {phaseOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {optionLabel(o)}
                </option>
              ))}
            </select>
          </label>
        )}
        {delayField && (
          <button type="button" className={chip(delay)} aria-pressed={delay} onClick={() => setDelay(!delay)}>
            {t('records.hasDelay')}
          </button>
        )}
        <span className="flex-1" />
        <div className="relative">
          <button type="button" aria-expanded={columnsOpen} onClick={() => setColumnsOpen(!columnsOpen)} className="flex h-[40px] items-center rounded-ui-lg border border-border bg-panel px-[14px] text-[14px] text-ink">
            {t('records.columns', { shown: shownColumns.length, total: columnChoices.length })}
          </button>
          {columnsOpen && (
            <div className="absolute end-0 top-[44px] z-30 flex max-h-[360px] w-[260px] flex-col overflow-y-auto rounded-ui-xl border border-border bg-panel p-2 shadow-elev-1">
              {columnChoices.map((f) => (
                <label key={f.id} className="flex min-h-[40px] items-center gap-2 rounded-ui-md px-2 text-[14px] text-ink hover:bg-field-muted">
                  <input type="checkbox" checked={columns.includes(f.code)} onChange={() => setColumns((c) => (c.includes(f.code) ? c.filter((x) => x !== f.code) : [...c, f.code]))} />
                  <bdi className="truncate">{fieldLabel(f)}</bdi>
                </label>
              ))}
            </div>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-start gap-4">
        <div className="min-w-0 flex-[999_1_520px]">
          {error ? (
            <ErrorState message={error} onRetry={() => (setError(null), setRetry((n) => n + 1))} />
          ) : !result ? (
            <LoadingState />
          ) : result.items.length === 0 ? (
            <EmptyState title={t('records.noMatch')} />
          ) : (
            <TableCard
              className="[&_table]:min-w-[760px]"
              footer={
                <Pager
                  from={from}
                  to={Math.min(from + (result?.items.length ?? 0) - 1, total)}
                  total={total}
                  onPrevious={page > 1 ? () => setPage(page - 1) : undefined}
                  onNext={from + PAGE_SIZE - 1 < total ? () => setPage(page + 1) : undefined}
                />
              }
            >
              <thead>
                <tr>
                  <th className={thClass}>{dateField ? <bdi>{fieldLabel(dateField)}</bdi> : t('records.sentBy')}</th>
                  {shownColumns.map((f) => (
                    <th key={f.id} className={thClass}>
                      <bdi>{fieldLabel(f)}</bdi>
                    </th>
                  ))}
                  {sections.map((sec) => (
                    <th key={sec.id} className={thClass}>
                      <bdi>{pickLanguage(sec.titleAr, sec.titleEn, i18n.language)}</bdi>
                    </th>
                  ))}
                  <th className={thClass}>{t('records.sentBy')}</th>
                </tr>
              </thead>
              <tbody>
                {(result?.items ?? []).map((r) => (
                  <ClickableRow key={r.id} selected={r.id === selected} onOpen={() => setSelected(r.id)} label={dateOf(r)}>
                    <td className={`${tdClass} whitespace-nowrap font-bold`}>{dateOf(r)}</td>
                    {shownColumns.map((f) => (
                      <td key={f.id} className={`${tdClass} whitespace-nowrap`}>
                        {cell(f, r)}
                      </td>
                    ))}
                    {sections.map((sec) => (
                      <td key={sec.id} className={tdClass}>
                        <BlocksCell form={form} record={r} sectionCode={sec.code} />
                      </td>
                    ))}
                    <td className={`${tdClass} whitespace-nowrap`}>
                      <bdi>{r.createdByName ?? '—'}</bdi>
                    </td>
                  </ClickableRow>
                ))}
              </tbody>
            </TableCard>
          )}
        </div>
        {selected && (
          <RecordDrawer
            key={selected}
            token={token}
            form={form}
            recordId={selected}
            onClose={() => setSelected(null)}
            onEdit={onEdit}
            onDeleted={() => {
              setSelected(null);
              setResult((r) => (r ? { items: r.items.filter((i) => i.id !== selected), total: r.total - 1 } : r));
            }}
          />
        )}
      </div>
    </div>
  );
}

/** "Thursday 5 November" - the record's date field (or when it was sent), without the year, as on 20. */
function shortDay(r: RecordItemDto, dateField: FieldDefinitionDto | undefined, locale: string) {
  const day = (dateField && storedToSaudiDate(r.values[dateField.code])) || sentDay(r);
  return dayLabel(day, locale);
}

/** The record drawer (20): answers, attachments with "+ Add", workflow status, Edit and Delete. */
function RecordDrawer({
  token,
  form,
  recordId,
  onClose,
  onEdit,
  onDeleted,
}: {
  token: string;
  form: FormDefinitionDto;
  recordId: string;
  onClose: () => void;
  onEdit: (record: RecordItemDto) => void;
  onDeleted: () => void;
}) {
  const { t, i18n } = useTranslation();
  const { fieldLabel } = useLocalized();
  const locale = localeFor(i18n.language);
  const [detail, setDetail] = useState<RecordDetailDto | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();
  const input = useRef<HTMLInputElement>(null);
  const [removing, setRemoving] = useState<string | null>(null);
  const attachmentField = form.publishedVersion?.fields.find((f) => f.isActive && f.fieldType === 'Attachment');

  const load = () =>
    api.submissions
      .get(token, form.id, recordId)
      .then(setDetail)
      .catch((err) => setError({ err, fallbackKey: 'formView.loadError' }));
  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, form.id, recordId]);

  async function upload(file: File) {
    if (!attachmentField) return;
    setBusy(true);
    setError(null);
    try {
      await api.files.upload(token, form.id, recordId, attachmentField.code, file);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'formRenderer.submitFailed' });
    } finally {
      setBusy(false);
    }
  }

  async function removeFile(fileId: string) {
    setBusy(true);
    setError(null);
    try {
      await api.files.delete(token, fileId);
      setRemoving(null);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'fieldEditor.deleteError' });
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    setBusy(true);
    setError(null);
    try {
      await api.submissions.delete(token, form.id, recordId);
      onDeleted();
    } catch (err) {
      setError({ err, fallbackKey: 'formView.deleteFailed' });
      setBusy(false);
    }
  }

  const ctx = { t, lang: i18n.language, locale };
  const record = detail?.record;
  const dateField = form.publishedVersion?.fields.find((f) => f.isActive && f.fieldType === 'DateTime');
  const title = record ? shortDay(record, dateField, locale) : '';
  const rows = record ? shownFields(form, record).filter((f) => f.fieldType !== 'Attachment').map((f) => ({ f, v: answerText(f, record, ctx) })).filter((x) => x.v !== null) : [];

  return (
    <Drawer
      open
      onClose={onClose}
      title={title}
      sub={record ? t('records.sentByAt', { name: record.createdByName ?? '—', time: sentTime(record, locale) }) : undefined}
      footer={
        record && (
          <>
            <Button size="desktop" block disabled={busy} onClick={() => onEdit(record)} className="!h-[44px]">
              {t('formView.editRecord')}
            </Button>
            <Button size="desktop" variant="danger" disabled={busy} onClick={() => setConfirming(true)} className="!h-[44px]">
              {t('formView.deleteRecord')}
            </Button>
          </>
        )
      }
    >
      {confirming && (
        <div className="flex flex-col gap-2 bg-danger-soft px-[18px] py-3">
          <p className="text-[14px] text-ink">{t('formView.confirmDelete')}</p>
          <div className="flex gap-2">
            <Button size="compact" variant="danger" disabled={busy} onClick={remove}>
              {busy ? t('formView.deleting') : t('formView.deleteRecord')}
            </Button>
            <Button size="compact" variant="link" onClick={() => setConfirming(false)}>
              {t('common.cancel')}
            </Button>
          </div>
        </div>
      )}
      <div className="flex flex-col px-[18px] pb-3 pt-1.5">
        {rows.map(({ f, v }) => (
          <div key={f.id} className="flex justify-between gap-3 border-b border-divider py-[9px] text-[14px]">
            <span className="shrink-0 text-ink-soft">
              <bdi>{fieldLabel(f)}</bdi>
            </span>
            <span className="min-w-0 text-end font-bold text-ink">
              <bdi>{v}</bdi>
            </span>
          </div>
        ))}
      </div>
      {record && form.code === 'projects' && <ProjectActions record={record} />}
      {record && record.sections && (
        <div className="flex flex-col gap-2 px-[18px] pb-3">
          <RecordBlocks form={form} record={record} compact />
        </div>
      )}
      {detail && (attachmentField || detail.attachments.length > 0) && (
        <div className="flex flex-col gap-2 px-[18px] pb-[14px] pt-1">
          <span className="text-[14px] font-bold text-ink">{t('records.attachmentsCount', { count: detail.attachments.length })}</span>
          <div className="flex flex-wrap gap-2">
            {detail.attachments.map((file) => (
              <div key={file.id} className="relative">
                <Thumbnail token={token} file={file} className="h-[64px] w-[84px] rounded-ui-md" />
                {/* As in the old attachments panel, an administrator can remove a file - asked twice. */}
                <button
                  type="button"
                  disabled={busy}
                  aria-label={removing === file.id ? `${t('common.remove')}?` : `${t('common.remove')} ${file.originalFileName}`}
                  onClick={() => (removing === file.id ? void removeFile(file.id) : setRemoving(file.id))}
                  className={`absolute -end-1.5 -top-1.5 flex h-6 min-w-6 items-center justify-center rounded-ui-pill border border-border px-1 text-[11px] font-bold ${removing === file.id ? 'bg-danger text-white' : 'bg-panel text-ink-soft'}`}
                >
                  {removing === file.id ? t('common.remove') : <Icon name="close" size={12} />}
                </button>
              </div>
            ))}
            {attachmentField && (
              <>
                <button type="button" disabled={busy} onClick={() => input.current?.click()} className="h-[64px] w-[84px] rounded-ui-md border-[1.5px] border-dashed border-border bg-panel text-[13px] font-bold text-accent">
                  {t('records.addAttachment')}
                </button>
                <input ref={input} type="file" hidden accept="image/jpeg,image/png,image/webp,image/heic,application/pdf" onChange={(e) => e.target.files?.[0] && void upload(e.target.files[0])} />
              </>
            )}
          </div>
        </div>
      )}
      {record && (
        <div className="px-[18px] py-3 empty:hidden">
          <WorkflowPanel token={token} recordId={record.id} onChanged={() => {}} />
        </div>
      )}
      {error && <p className="px-[18px] py-2 text-[14px] text-danger">{error}</p>}
    </Drawer>
  );
}

/**
 * A project's Pause / Resume (2026-11-10, administrators): opens the pause log with the project,
 * the action and today's date filled in - the log's rules (no double pause, status change) run
 * on the server. Also the link to the project's stage progress.
 */
function ProjectActions({ record }: { record: RecordItemDto }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const shell = useOptionalShell();
  const forms = shell?.forms;
  const isAdmin = shell?.isAdmin ?? false;
  const pauseForm = forms?.find((f) => f.code === 'project-pause-log');
  const status = String(record.values.status ?? '');
  if (!isAdmin) return null;
  const action = status === 'on_hold' ? 'resume' : 'pause';
  return (
    <div className="flex flex-wrap gap-2 px-[18px] pb-3">
      {pauseForm && status !== 'completed' && status !== 'cancelled' && (
        <Button
          size="compact"
          variant={action === 'pause' ? 'secondary' : 'primary'}
          onClick={() => navigate(`/forms/${pauseForm.id}`, { state: { prefill: { project: record.id, action, date: todayIso() } } })}
        >
          {action === 'pause' ? t('stageProgress.pauseProject') : t('stageProgress.resumeProject')}
        </Button>
      )}
      <Button size="compact" variant="link" onClick={() => navigate(`/admin/projects/${record.id}/progress`)}>
        {t('stageProgress.openProgress')}
      </Button>
    </div>
  );
}

/** A section's blocks in one table cell: what each block was about (its first answer) and, when the
 *  blocks say, whether the day's plan was met in each. */
function BlocksCell({ form, record, sectionCode }: { form: FormDefinitionDto; record: RecordItemDto; sectionCode: string }) {
  const { t, i18n } = useTranslation();
  const ctx = { t, lang: i18n.language, locale: localeFor(i18n.language) };
  const section = shownSections(form, record, ctx).find((s) => s.section.code === sectionCode);
  if (!section) return <>—</>;
  return (
    <span className="flex flex-col gap-1">
      {section.blocks.map((b) => {
        const met = record.sections?.[sectionCode]?.find((x) => x.index === b.index)?.values[BLOCK_PLAN_FIELD];
        return (
          <span key={b.index} className="flex items-center gap-1.5 whitespace-nowrap">
            <bdi>{b.rows[0]?.value ?? '—'}</bdi>
            {typeof met === 'boolean' && (
              <span className={`rounded-ui-pill px-[7px] py-[1px] text-[11px] font-bold ${met ? 'bg-success-soft text-success' : 'bg-danger-soft text-danger'}`}>
                {met ? t('entries.tagPlanMet') : t('entries.tagPlanNotMet')}
              </span>
            )}
          </span>
        );
      })}
    </span>
  );
}
