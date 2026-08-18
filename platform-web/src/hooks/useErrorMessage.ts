import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiErrorMessage } from '../api/errorMessage';

export type ErrorInput =
  | { key: string; params?: Record<string, unknown> }
  | { err: unknown; fallbackKey: string }
  | null;

/**
 * Like `useState<string | null>` for an error banner, except the message is resolved from
 * its source (an i18n key, or a caught error) on every render instead of once at the moment
 * it's set - so a language switch immediately re-renders any error already on screen instead
 * of leaving it stuck in whatever language was active when it was first shown.
 */
export function useErrorMessage(): [string | null, (input: ErrorInput) => void] {
  const { t } = useTranslation();
  const [input, setInput] = useState<ErrorInput>(null);

  if (input === null) return [null, setInput];
  if ('err' in input) return [apiErrorMessage(input.err, t, input.fallbackKey), setInput];
  return [t(input.key, input.params), setInput];
}
