import { useTranslation } from 'react-i18next';
import { formatCalendarDate } from '../forms/fieldValues';
import type { AssignmentDayStatus } from '../types';
import { StatusLed } from './StatusLed';

const STATUS_TONE = { filled: 'success', missed: 'danger', pending: 'accent' } as const;

/** Filled (green), not filled (red), due today (blue). */
export function AssignmentStatusBadge({ status }: { status: AssignmentDayStatus }) {
  const { t } = useTranslation();
  return <StatusLed label={t(`assignments.status.${status}`)} tone={STATUS_TONE[status]} />;
}

/**
 * Dates and times for the assignment pages, in the active language. Times are shown in Saudi
 * time whatever the device's own time zone is - "filled at 09:30" means 09:30 on site.
 */
export function useAssignmentFormat() {
  const { i18n } = useTranslation();
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  const weekday = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });
  const dateTime = new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'Asia/Riyadh',
  });
  const time = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Riyadh' });

  return {
    /** "Tue 3 Nov 2026" for a Saudi calendar day ("2026-11-03"). */
    day: (iso: string) => `${weekday.format(new Date(`${iso}T00:00:00Z`))} ${formatCalendarDate(iso, i18n.language)}`,
    date: (iso: string) => formatCalendarDate(iso, i18n.language),
    /** "3 Nov, 09:30" in Saudi time, for a UTC instant. */
    dateTime: (utc: string) => dateTime.format(new Date(utc)),
    /** "09:30" in Saudi time. */
    time: (utc: string) => time.format(new Date(utc)),
  };
}
