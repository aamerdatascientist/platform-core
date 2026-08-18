import type { TFunction } from 'i18next';
import i18n from '../i18n';
import { ApiError } from './client';

/**
 * Resolves a caught error to a message in the currently-active language, for the common
 * "show the backend's error, or a generic fallback for anything that isn't a clean API
 * error response" pattern used throughout the app.
 *
 * An ApiError with a `code` the frontend recognizes (apiErrors.<code> exists in the i18n
 * resources) is fully localized. An ApiError without a recognized code falls back to its
 * `message` - the backend's raw English text, since not every backend exception has been
 * given a translatable code yet. Anything that isn't an ApiError at all (a network failure,
 * a JS-level exception) uses the caller's own fallbackKey.
 */
export function apiErrorMessage(err: unknown, t: TFunction, fallbackKey: string): string {
  if (err instanceof ApiError) {
    if (err.code && i18n.exists(`apiErrors.${err.code}`)) {
      return t(`apiErrors.${err.code}`);
    }
    return err.message;
  }
  return t(fallbackKey);
}
