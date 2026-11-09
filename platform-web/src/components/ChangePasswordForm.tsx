import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api/client';
import { setTokens } from '../auth/tokenStore';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { Icon } from './icons';
import { Button, FieldShell, TextInput } from './ui';

interface ChangePasswordFormProps {
  token: string;
}

const MIN_LENGTH = 10;

/**
 * Change your own password - open to everyone signed in, project managers included; shown
 * at /account (Settings > Account > Change password). On success the server signs out every other device of this account
 * and hands this one a fresh sign-in, which is stored straight away so the page carries on
 * working.
 */
export function ChangePasswordForm({ token }: ChangePasswordFormProps) {
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

  const fields: [string, string, string, (v: string) => void, string][] = [
    [t('account.currentPassword'), current, 'current-password', setCurrent, 'current'],
    [t('account.newPassword', { min: MIN_LENGTH }), next, 'new-password', setNext, 'next'],
    [t('account.repeatPassword'), repeat, 'new-password', setRepeat, 'repeat'],
  ];

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-[18px]" noValidate>
      <p className="text-[15px] text-ink-soft">{t('account.subtitle')}</p>
      {fields.map(([label, value, autoComplete, set, key]) => (
        <FieldShell key={key} label={label}>
          {({ inputId }) => (
            <TextInput id={inputId} type="password" autoComplete={autoComplete} value={value} onChange={(e) => set(e.target.value)} />
          )}
        </FieldShell>
      ))}
      {error && (
        <p className="flex items-start gap-1.5 rounded-ui-lg bg-danger-soft px-3 py-2 text-[14px] text-danger" role="alert">
          <Icon name="alert" size={16} className="mt-0.5" />
          <span>{error}</span>
        </p>
      )}
      {done && (
        <p className="flex items-center gap-2 rounded-ui-lg bg-success-soft px-3 py-2 text-[15px] text-success-strong" role="status">
          <Icon name="check" size={18} />
          {t('account.changed')}
        </p>
      )}
      <Button type="submit" disabled={saving} block>
        {saving ? t('common.saving') : t('account.change')}
      </Button>
    </form>
  );
}
