import type { TFunction } from 'i18next';
import type { DropdownOption, FieldDefinitionDto, FormDefinitionDto, RecordItemDto } from '../types';
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

export function answerText(field: FieldDefinitionDto, record: RecordItemDto, ctx: AnswerContext): string | null {
  const value = record.values[field.code];
  if (value === null || value === undefined || value === '') return null;
  switch (field.fieldType) {
    case 'Boolean':
      return value === true || value === 'true' ? ctx.t('common.yes') : ctx.t('common.no');
    case 'Dropdown': {
      const option = parseOptions(field.optionsJson).find((o: DropdownOption) => o.value === String(value));
      return option ? pickLanguage(option.label, option.labelEn, ctx.lang) : String(value);
    }
    case 'Lookup':
      return record.lookupLabels[field.code] ?? null;
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
  const fields = [...(form.publishedVersion?.fields ?? [])].filter((f) => f.isActive).sort((a, b) => a.displayOrder - b.displayOrder);
  const asText = recordToFormValues(fields, record.values);
  return fields.filter((f) => isFieldVisible(f, asText));
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
  return picked.map((f) => answerText(f, record, ctx)).join(' · ');
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

export function entryTags(record: RecordItemDto, t: TFunction): EntryTag[] {
  const tags: EntryTag[] = [];
  const plan = record.values[PLAN_FIELD];
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
