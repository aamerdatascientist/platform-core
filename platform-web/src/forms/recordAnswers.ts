import type { TFunction } from 'i18next';
import type { DropdownOption, FieldDefinitionDto, FormDefinitionDto, FormSectionDto, RecordBlockDto, RecordItemDto } from '../types';
import { isFieldVisible, parseOptions, recordToFormValues, storedToSaudiDate } from './fieldValues';
import { pickLanguage } from './localized';

/**
 * A stored record as people read it (My entries 08, its detail page, the Records table and
 * drawer 20): option and Lookup labels instead of codes and ids, Yes/No, dates on the Saudi
 * calendar. One place, so every page shows an answer the same way.
 */
export interface AnswerContext {
  t: TFunction;
  lang: string;
  /** "ar-u-nu-latn" for Arabic (Latin digits, as the rest of the app). */
  locale: string;
}

export function localeFor(lang: string) {
  return lang.startsWith('ar') ? 'ar-u-nu-latn' : lang;
}

/** A provider label from the server: "Arabic\u001FEnglish" when both exist. */
export function serverLabel(label: string, lang: string): string {
  const [ar, en] = label.split('\u001F');
  return pickLanguage(ar, en ?? null, lang);
}

export function answerText(field: FieldDefinitionDto, record: RecordItemDto, ctx: AnswerContext): string | null {
  return valueText(field, record.values[field.code], record.lookupLabels[field.code], ctx);
}

/** One block's answer (labels from the block: lookups, multi lookups and provider choices). */
export function blockAnswerText(field: FieldDefinitionDto, block: RecordBlockDto, ctx: AnswerContext): string | null {
  return valueText(field, block.values[field.code], block.labels[field.code], ctx);
}

function valueText(field: FieldDefinitionDto, value: unknown, label: string | undefined, ctx: AnswerContext): string | null {
  if (value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0)) return null;
  if (label && (field.optionsProvider || field.fieldType === 'MultiLookup')) return serverLabel(label, ctx.lang);
  switch (field.fieldType) {
    case 'MultiSelect': {
      const options = parseOptions(field.optionsJson);
      return (Array.isArray(value) ? value : [value])
        .map((v) => options.find((o) => o.value === String(v)))
        .map((o, i) => (o ? pickLanguage(o.label, o.labelEn, ctx.lang) : String((value as unknown[])[i])))
        .join('، ');
    }
    case 'MultiLookup':
      return null;
    case 'Boolean':
      return value === true || value === 'true' ? ctx.t('common.yes') : ctx.t('common.no');
    case 'Dropdown': {
      const option = parseOptions(field.optionsJson).find((o: DropdownOption) => o.value === String(value));
      return option ? pickLanguage(option.label, option.labelEn, ctx.lang) : String(value);
    }
    case 'Lookup':
      return label ?? null;
    case 'DateTime': {
      const day = storedToSaudiDate(value);
      return day ? `${dayLabel(day, ctx.locale)} ${new Intl.DateTimeFormat(ctx.locale, { year: 'numeric', timeZone: 'UTC' }).format(new Date(`${day}T00:00:00Z`))}` : null;
    }
    case 'Attachment':
      return null;
    default:
      return String(value);
  }
}

/** The fields that were shown when the record was sent (conditions met), in builder order. */
export function shownFields(form: FormDefinitionDto, record: RecordItemDto): FieldDefinitionDto[] {
  // Archived fields stay readable on the records that answered them (old phase-based reports).
  const fields = [...(form.publishedVersion?.fields ?? [])]
    .filter((f) => !f.sectionId && (f.isActive || (record.values[f.code] !== null && record.values[f.code] !== undefined && record.values[f.code] !== '')))
    .sort((a, b) => a.displayOrder - b.displayOrder);
  const asText = recordToFormValues(fields, record.values);
  return fields.filter((f) => isFieldVisible(f, asText));
}

/** A record's repeatable sections as people read them: each block's answered fields, in order. */
export function shownSections(form: FormDefinitionDto, record: RecordItemDto, ctx: AnswerContext) {
  const sections: FormSectionDto[] = form.publishedVersion?.sections ?? [];
  return sections
    .map((section) => {
      const fields = (form.publishedVersion?.fields ?? []).filter((f) => f.sectionId === section.id).sort((a, b) => a.displayOrder - b.displayOrder);
      const blocks = (record.sections?.[section.code] ?? []).map((block) => ({
        index: block.index,
        rows: fields.map((f) => ({ field: f, value: blockAnswerText(f, block, ctx) })).filter((r) => r.value !== null) as { field: FieldDefinitionDto; value: string }[],
      }));
      return { section, title: pickLanguage(section.titleAr, section.titleEn, ctx.lang), blocks };
    })
    .filter((s) => s.blocks.length > 0);
}

/**
 * The summary line on an entry card (08): the form's 2-3 most telling answers - "project ·
 * phase · unit" for the daily report, "material · quantity · project" for stock. Lookups that
 * aren't conditional come first, then the selectors other fields depend on (phase), then the
 * answers those selectors revealed (the unit), then plain text/number/choice answers; dates,
 * yes/no, long text and photos are left out. Shown in builder order.
 */
export function entrySummary(form: FormDefinitionDto, record: RecordItemDto, ctx: AnswerContext): string {
  const fields = shownFields(form, record);
  const controllers = new Set(fields.map((f) => f.visibleWhenFieldCode).filter(Boolean));
  const rank = (f: FieldDefinitionDto) => {
    if (f.fieldType === 'Lookup' && !f.visibleWhenFieldCode) return 0;
    if (f.fieldType === 'Dropdown' && controllers.has(f.code)) return 1;
    if (f.visibleWhenFieldCode && f.fieldType !== 'Boolean' && f.fieldType !== 'LongText') return 2;
    if (['ShortText', 'Number', 'Decimal', 'Dropdown', 'Lookup'].includes(f.fieldType)) return 3;
    return 9;
  };
  const picked = fields
    .filter((f) => rank(f) < 9 && answerText(f, record, ctx) !== null)
    .sort((a, b) => rank(a) - rank(b) || a.displayOrder - b.displayOrder)
    .slice(0, 3)
    .sort((a, b) => a.displayOrder - b.displayOrder);
  // A report with blocks: two header answers, then what each block was about (its first answer - the stage).
  const blocks = shownSections(form, record, ctx)[0]?.blocks ?? [];
  const about = blocks.map((b) => b.rows[0]?.value).filter(Boolean);
  const parts = picked.slice(0, about.length > 0 ? 2 : 3).map((f) => answerText(f, record, ctx));
  if (about.length > 0) parts.push(about.join(' + '));
  return parts.join(' · ');
}

export type EntryTag = { tone: 'success' | 'danger' | 'warning' | 'info'; label: string };

/**
 * Tag pills (08, and the Records table's two flagged columns): planned work done ("Plan met" /
 * "Plan not met"), a delay ("Delay"), and the attachment count. The two daily-report answers are
 * recognised by field code - the same codes ExecutiveOverviewLoader reads (`plan_completed_today`,
 * `had_delay`), so the dashboard and these pills can never disagree about them.
 */
export const PLAN_FIELD = 'plan_completed_today';
export const DELAY_FIELD = 'had_delay';
/** The stage block's own "was today's planned work met?" (daily report v2, 2026-11-10). */
export const BLOCK_PLAN_FIELD = 'plan_met';

export function entryTags(record: RecordItemDto, t: TFunction): EntryTag[] {
  const tags: EntryTag[] = [];
  // v2 daily reports: the blocks' plan_met (met when every block met it).
  const blockPlans = Object.values(record.sections ?? {}).flat().map((b) => b.values[BLOCK_PLAN_FIELD]).filter((v) => typeof v === 'boolean');
  const plan = blockPlans.length > 0 ? blockPlans.every((v) => v === true) : record.values[PLAN_FIELD];
  if (plan === true) tags.push({ tone: 'success', label: t('entries.tagPlanMet') });
  if (plan === false) tags.push({ tone: 'danger', label: t('entries.tagPlanNotMet') });
  if (record.values[DELAY_FIELD] === true) tags.push({ tone: 'warning', label: t('entries.tagDelay') });
  if (record.attachmentCount > 0) tags.push({ tone: 'info', label: t('sent.photos', { count: record.attachmentCount }) });
  return tags;
}

/** The Saudi calendar day a record was sent on ("yyyy-mm-dd"). */
export function sentDay(record: RecordItemDto) {
  return storedToSaudiDate(record.createdAtUtc)!;
}

export function sentTime(record: RecordItemDto, locale: string) {
  return new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', timeZone: 'Asia/Riyadh' }).format(new Date(record.createdAtUtc));
}

/** "Saturday 7 November" / "السبت 7 نوفمبر" for a "yyyy-mm-dd" day - weekday, day, month, no
 *  comma, the same order in both languages (08, 20). */
export function dayLabel(day: string, locale: string) {
  const parts = new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).formatToParts(new Date(`${day}T00:00:00Z`));
  const part = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  return `${part('weekday')} ${part('day')} ${part('month')}`;
}
