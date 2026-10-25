import type { TFunction } from 'i18next';
import i18n from '../i18n';
import { ApiError } from './client';

/**
 * Resolves a caught error to a message in the currently-active language, for the common
 * "show the backend's error, or a generic fallback for anything that isn't a clean API
 * error response" pattern used throughout the app.
 *
 * In order: an ApiError with a `code` the frontend recognizes (apiErrors.<code>) is fully
 * localized; the common HTTP statuses get a plain explanation (errors.status.<n>), since the
 * server's own text for them is technical ("Entity 'FormDefinition' (...) was not found.");
 * any other 400 shows the server's specific English text in English and a translated
 * "check what you entered" in Arabic, rather than English inside an Arabic page. No response
 * at all (offline, the server unreachable) is errors.offline; anything else uses the
 * caller's fallbackKey.
 */
export function apiErrorMessage(err: unknown, t: TFunction, fallbackKey: string): string {
  if (err instanceof ApiError) {
    if (err.code && i18n.exists(`apiErrors.${err.code}`)) return t(`apiErrors.${err.code}`);
    const statusKey = statusMessageKey(err.status);
    if (statusKey) return t(statusKey);
    if (err.status === 400 && !i18n.language.startsWith('en')) return t('errors.badInput');
    return err.message;
  }
  if (isNetworkError(err)) return t('errors.offline');
  return t(fallbackKey);
}

export type StatusKind = 'sessionEnded' | 'forbidden' | 'notFound' | 'serverError' | 'offline';

/** Which full-page explanation (components/StatusPage) an error that stops a whole page calls for, if any. */
export function statusKindOf(err: unknown): StatusKind | null {
  if (err instanceof ApiError) {
    if (err.status === 401) return 'sessionEnded';
    if (err.status === 403) return 'forbidden';
    if (err.status === 404) return 'notFound';
    if (err.status >= 500) return 'serverError';
    return null;
  }
  return isNetworkError(err) ? 'offline' : null;
}

function statusMessageKey(status: number): string | null {
  if (status === 401 || status === 403 || status === 404 || status === 413 || status === 429) return `errors.status.${status}`;
  if (status >= 500) return 'errors.status.500';
  return null;
}

// fetch() rejects with a TypeError when no response arrives at all: offline, DNS, the API down.
function isNetworkError(err: unknown): boolean {
  return err instanceof TypeError || (typeof navigator !== 'undefined' && navigator.onLine === false);
}
