import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { statusKindOf } from '../api/errorMessage';
import type { StatusKind } from '../api/errorMessage';
import { Icon } from '../components/icons';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { StatusPage } from '../components/StatusPage';
import { IconButton } from '../components/ui';
import { useLocalized } from '../forms/localized';
import { answerText, dayLabel, localeFor, sentDay, sentTime, shownFields } from '../forms/recordAnswers';
import type { FileMetadataDto, FormDefinitionDto, RecordDetailDto } from '../types';

/**
 * One of my entries, read-only (08 -> detail): laid out like the review on 06 - a card per step
 * with label/value rows - then its photos. Editing stays with administrators (Records tab).
 */
export function EntryDetail({ token }: { token: string }) {
  const { t, i18n } = useTranslation();
  const { formId, recordId } = useParams<{ formId: string; recordId: string }>();
  const navigate = useNavigate();
  const { formName, fieldLabel, stepTitle } = useLocalized();
  const locale = localeFor(i18n.language);
  const [form, setForm] = useState<FormDefinitionDto | null>(null);
  const [detail, setDetail] = useState<RecordDetailDto | null>(null);
  const [failure, setFailure] = useState<StatusKind | null>(null);

  useEffect(() => {
    if (!formId || !recordId) return;
    Promise.all([api.forms.get(token, formId), api.submissions.get(token, formId, recordId)])
      .then(([f, d]) => {
        setForm(f);
        setDetail(d);
      })
      .catch((err) => setFailure(statusKindOf(err) ?? 'serverError'));
  }, [token, formId, recordId]);

  const cards = useMemo(() => {
    if (!form || !detail) return [];
    const ctx = { t, lang: i18n.language, locale };
    const fields = shownFields(form, detail.record).filter((f) => f.fieldType !== 'Attachment');
    const steps = [...(form.publishedVersion?.steps ?? [])].sort((a, b) => a.displayOrder - b.displayOrder);
    const known = new Set(steps.map((s) => s.id));
    const groups = steps.length
      ? steps.map((s) => ({ id: s.id, title: stepTitle(s), fields: fields.filter((f) => (f.stepId && known.has(f.stepId) ? f.stepId : steps[0].id) === s.id) }))
      : [{ id: 'all', title: formName(form), fields }];
    return groups
      .map((g) => ({ ...g, rows: g.fields.map((f) => ({ id: f.id, label: fieldLabel(f), value: answerText(f, detail.record, ctx) })).filter((r) => r.value !== null) }))
      .filter((g) => g.rows.length > 0);
  }, [form, detail, t, i18n.language, locale, formName, fieldLabel, stepTitle]);

  if (failure) return <StatusPage kind={failure} />;
  if (!form || !detail) return <LoadingSpinner />;

  const day = dayLabel(sentDay(detail.record), locale);
  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-5">
      <header className="flex items-center gap-2">
        <IconButton label={t('steps.back')} onClick={() => (window.history.length > 1 ? navigate(-1) : navigate('/entries'))}>
          <Icon name="arrowBack" size={22} />
        </IconButton>
        <div className="flex min-w-0 flex-col">
          <h1 className="truncate text-[22px] font-extrabold text-ink">
            <bdi>{formName(form)}</bdi>
          </h1>
          <span className="text-[14px] text-ink-soft">
            {day} · {sentTime(detail.record, locale)}
          </span>
        </div>
      </header>

      {cards.map((card) => (
        <section key={card.id} className="flex flex-col rounded-[18px] border border-border bg-panel">
          <h2 className="px-4 pb-2 pt-3 text-[16px] font-bold text-ink">
            <bdi>{card.title}</bdi>
          </h2>
          {card.rows.map((r) => (
            <div key={r.id} className="flex justify-between gap-3 border-t border-divider px-4 py-[10px]">
              <span className="shrink-0 text-[15px] text-ink-soft">
                <bdi>{r.label}</bdi>
              </span>
              <span className="min-w-0 text-end text-[15px] font-bold text-ink">
                <bdi>{r.value}</bdi>
              </span>
            </div>
          ))}
        </section>
      ))}

      {detail.attachments.length > 0 && (
        <section className="flex flex-col gap-[10px]">
          <h2 className="text-[15px] font-bold text-ink">{t('records.attachmentsCount', { count: detail.attachments.length })}</h2>
          <div className="grid grid-cols-3 gap-2">
            {detail.attachments.map((file) => (
              <Thumbnail key={file.id} token={token} file={file} className="h-[100px] rounded-ui-xl" />
            ))}
          </div>
        </section>
      )}
    </div>
  );
}

/** A photo (or a file tile) that opens the full file in a new tab. Download links expire, so it asks for one when shown. */
export function Thumbnail({ token, file, className = '' }: { token: string; file: FileMetadataDto; className?: string }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    api.files
      .getDownloadUrl(token, file.id)
      .then((r) => alive && setUrl(r.url))
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [token, file.id]);
  const image = file.contentType.startsWith('image/');
  return (
    <a
      href={url ?? undefined}
      target="_blank"
      rel="noreferrer"
      aria-label={file.originalFileName}
      className={`flex items-center justify-center overflow-hidden border border-border bg-field-muted text-ink-soft ${className}`}
    >
      {image && url ? <img src={url} alt="" className="h-full w-full object-cover" /> : <Icon name={image ? 'camera' : 'paperclip'} size={22} />}
    </a>
  );
}
