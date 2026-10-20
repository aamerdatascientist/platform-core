import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api/client';
import { setTokens } from '../auth/tokenStore';
import { useErrorMessage } from '../hooks/useErrorMessage';

interface AccountProps {
  token: string;
}

const MIN_LENGTH = 10;

/**
 * Change your own password - open to everyone signed in, project managers included. On
 * success the server signs out every other device of this account and hands this one a fresh
 * sign-in, which is stored straight away so the page carries on working.
 */
export function Account({ token }: AccountProps) {
  const { t } = useTranslation();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [repeat, setRepeat] = useState('');
  const [saving, setSaving] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useErrorMessage();

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setDone(false);
    if (!current || !next) return setError({ key: 'account.allRequired' });
    if (next.length < MIN_LENGTH) return setError({ key: 'account.tooShort', params: { min: MIN_LENGTH } });
    if (next !== repeat) return setError({ key: 'account.mismatch' });
    if (next === current) return setError({ key: 'account.sameAsCurrent' });

    setSaving(true);
    try {
      const tokens = await api.auth.changePassword(token, current, next);
      setTokens(tokens);
      setCurrent('');
      setNext('');
      setRepeat('');
      setDone(true);
    } catch (err) {
      if (err instanceof ApiError && err.status === 400 && err.errors?.CurrentPassword) setError({ key: 'account.wrongCurrent' });
      else setError({ err, fallbackKey: 'account.failed' });
    } finally {
      setSaving(false);
    }
  }

  const inputClass = 'w-full rounded border border-border bg-bg px-3 py-2 text-sm focus:border-accent focus:outline-none';

  return (
    <div className="max-w-md space-y-6">
      <div>
        <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{t('account.title')}</h2>
        <p className="mt-1 text-sm text-ink-soft">{t('account.subtitle')}</p>
      </div>
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <label className="block text-sm font-medium text-ink">
          {t('account.currentPassword')}
          <input
            type="password"
            autoComplete="current-password"
            className={`mt-1 ${inputClass}`}
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
          />
        </label>
        <label className="block text-sm font-medium text-ink">
          {t('account.newPassword', { min: MIN_LENGTH })}
          <input
            type="password"
            autoComplete="new-password"
            className={`mt-1 ${inputClass}`}
            value={next}
            onChange={(e) => setNext(e.target.value)}
          />
        </label>
        <label className="block text-sm font-medium text-ink">
          {t('account.repeatPassword')}
          <input
            type="password"
            autoComplete="new-password"
            className={`mt-1 ${inputClass}`}
            value={repeat}
            onChange={(e) => setRepeat(e.target.value)}
          />
        </label>
        {error && (
          <p className="text-sm text-danger" role="alert">
            {error}
          </p>
        )}
        {done && (
          <p className="rounded border border-success p-3 text-sm text-ink" role="status">
            {t('account.changed')}
          </p>
        )}
        <button
          type="submit"
          disabled={saving}
          className="w-full rounded bg-accent px-4 py-2 text-sm font-medium text-accent-ink hover:opacity-90 disabled:opacity-50"
        >
          {saving ? t('common.saving') : t('account.change')}
        </button>
      </form>
    </div>
  );
}
