import type { DropdownOption, FieldDefinitionDto } from '../types';

/**
 * Field metadata and value helpers shared by the form, the records table and the record
 * editor. Several of these mirror the backend's SubmissionValueValidator and must keep giving
 * the same answers (see CLAUDE.md's "three places this has to stay in sync").
 */

/** FieldDefinition.ValidationRulesJson, as SubmissionValueValidator reads it. */
export interface FieldRules {
  min: number | null;
  max: number | null;
  /** Min is a strict "more than" (a quantity that must be above zero). */
  minExclusive: boolean;
  regex: string | null;
  noFutureDates: boolean;
  /** No other record may hold the same text - checked by the server only, since it needs the other records. */
  unique: boolean;
  /** Unique only among records with the same value in this sibling field (zone codes within one project). */
  uniqueWithin: string | null;
}

export function parseRules(json: string | null | undefined): FieldRules {
  const empty: FieldRules = { min: null, max: null, minExclusive: false, regex: null, noFutureDates: false, unique: false, uniqueWithin: null };
  if (!json) return empty;
  try {
    // Property names are matched case-insensitively, like the backend does.
    const raw = Object.fromEntries(
      Object.entries(JSON.parse(json) as Record<string, unknown>).map(([k, v]) => [k.toLowerCase(), v]),
    );
    const num = (v: unknown) => (typeof v === 'number' && Number.isFinite(v) ? v : null);
    return {
      min: num(raw.min),
      max: num(raw.max),
      minExclusive: raw.minexclusive === true,
      regex: typeof raw.regex === 'string' && raw.regex.length > 0 ? raw.regex : null,
      noFutureDates: raw.nofuturedates === true,
      unique: raw.unique === true,
      uniqueWithin: typeof raw.uniquewithin === 'string' && raw.uniquewithin.length > 0 ? raw.uniquewithin : null,
    };
  } catch {
    return empty;
  }
}

/**
 * The allowed values of a visibility condition - same reading as the backend's
 * SubmissionValueValidator.ParseAllowedValues: a JSON array, or a single JSON string (the
 * live Daily Progress Report stores some that way), or a string holding an array, or a bare
 * true/false/number. Compared exactly, never as a substring.
 */
export function parseAllowedValues(json: string | null | undefined): string[] {
  if (!json) return [];
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch {
    return [];
  }
  if (typeof parsed === 'string') {
    return parsed.trimStart().startsWith('[') ? parseAllowedValues(parsed) : [parsed];
  }
  if (Array.isArray(parsed)) {
    return parsed.filter((v) => ['string', 'number', 'boolean'].includes(typeof v)).map((v) => String(v));
  }
  if (['number', 'boolean'].includes(typeof parsed)) return [String(parsed)];
  return [];
}

/** A field with no condition is always visible; otherwise only when its controlling field's
 *  current value is one of the allowed values. */
export function isFieldVisible(field: FieldDefinitionDto, values: Record<string, string>): boolean {
  if (!field.visibleWhenFieldCode) return true;
  const controllingValue = values[field.visibleWhenFieldCode];
  if (controllingValue === undefined || controllingValue === '') return false;
  return parseAllowedValues(field.visibleWhenValuesJson).includes(controllingValue);
}

export function parseOptions(optionsJson: string | null): DropdownOption[] {
  if (!optionsJson) return [];
  try {
    const parsed = JSON.parse(optionsJson) as DropdownOption[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

const SAUDI_OFFSET_MS = 3 * 60 * 60 * 1000;

/**
 * The Saudi calendar date ("yyyy-mm-dd") of a stored form date. Dates are entered as Saudi
 * local days and stored as that local midnight in UTC (10 Oct is stored as 9 Oct 21:00Z), so
 * printing the stored value as-is showed every date a day early.
 */
export function storedToSaudiDate(value: unknown): string | null {
  if (typeof value !== 'string' || value === '') return null;
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return value;
  const time = Date.parse(value);
  if (Number.isNaN(time)) return null;
  return new Date(time + SAUDI_OFFSET_MS).toISOString().slice(0, 10);
}

/** Today on the Saudi calendar, whatever the device's own time zone. */
export function saudiTodayIso(): string {
  return new Date(Date.now() + SAUDI_OFFSET_MS).toISOString().slice(0, 10);
}

/** "20 Oct 2026" / "20 أكتوبر 2026" for a "yyyy-mm-dd" calendar date. */
export function formatCalendarDate(iso: string, language: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!match) return iso;
  const locale = language.startsWith('ar') ? 'ar-u-nu-latn' : language;
  return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(
    new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))),
  );
}

/** A stored record's values as the form's string inputs hold them, for editing a record. */
export function recordToFormValues(fields: FieldDefinitionDto[], values: Record<string, unknown>): Record<string, string> {
  const result: Record<string, string> = {};
  for (const field of fields) {
    const value = values[field.code];
    if (value === null || value === undefined || field.fieldType === 'Attachment') continue;
    if (field.fieldType === 'DateTime') {
      const date = storedToSaudiDate(value);
      if (date) result[field.code] = date;
    } else if (field.fieldType === 'Boolean') {
      result[field.code] = value === true || value === 'true' ? 'true' : 'false';
    } else {
      result[field.code] = String(value);
    }
  }
  return result;
}

export type FieldProblem = { key: string; params?: Record<string, unknown> };

/**
 * The same value checks the server makes (SubmissionValueValidator), so the person sees the
 * problem next to the field, in their language, before anything is sent. Returns null when
 * the value is fine. `raw` is the input's text; an empty value is the required check's job.
 */
export function checkValue(field: FieldDefinitionDto, raw: string): FieldProblem | null {
  const value = raw.trim();
  if (value === '') return null;
  const rules = parseRules(field.validationRulesJson);

  if (field.fieldType === 'Number' || field.fieldType === 'Decimal') {
    // Plain digits only: the browser's number box also accepts "1e3" (= 1000) and "+5".
    const pattern = field.fieldType === 'Number' ? /^-?\d+$/ : /^-?(\d+\.?\d*|\.\d+)$/;
    if (!pattern.test(value)) {
      return { key: field.fieldType === 'Number' ? 'fieldRules.wholeNumber' : 'fieldRules.number' };
    }
    const n = Number(value);
    if (field.fieldType === 'Number' && Math.abs(n) > 2147483647) return { key: 'fieldRules.tooLarge' };
    if (field.fieldType === 'Decimal' && Math.abs(n) >= 1e14) return { key: 'fieldRules.tooLarge' };
    if (rules.min !== null) {
      if (rules.minExclusive && n <= rules.min) return { key: 'fieldRules.moreThan', params: { min: rules.min } };
      if (!rules.minExclusive && n < rules.min) return { key: 'fieldRules.atLeast', params: { min: rules.min } };
    }
    if (rules.max !== null && n > rules.max) return { key: 'fieldRules.atMost', params: { max: rules.max } };
    return null;
  }

  if (field.fieldType === 'DateTime') {
    if (rules.noFutureDates && value > saudiTodayIso()) return { key: 'fieldRules.noFuture' };
    return null;
  }

  if (field.fieldType === 'ShortText' && value.length > 400) return { key: 'fieldRules.tooLong', params: { max: 400 } };

  if ((field.fieldType === 'ShortText' || field.fieldType === 'LongText') && rules.regex) {
    try {
      if (!new RegExp(rules.regex).test(value)) return { key: 'fieldRules.format' };
    } catch {
      // A malformed admin pattern never blocks the person filling the form - same as the server.
    }
  }
  return null;
}
