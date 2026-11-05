import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { UserSummaryDto } from '../types';

/**
 * A user's Telegram connection (Users page). People usually connect themselves from Settings;
 * this is for an administrator to make a link and send it to them (e.g. on WhatsApp), check
 * that messages arrive, or disconnect someone.
 */
export function UserTelegramPanel({
  token,
  user,
  onChanged,
  onClose,
}: {
  token: string;
  user: UserSummaryDto;
  onChanged: () => Promise<void> | void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [link, setLink] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();
  const [testResult, setTestResult] = useState<{ success: boolean; error: string | null } | null>(null);

  async function run(action: () => Promise<void>) {
    setError(null);
    setTestResult(null);
    setBusy(true);
    try {
      await action();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.telegramError' });
    } finally {
      setBusy(false);
    }
  }

  const handleCreateLink = () =>
    run(async () => {
      const result = await api.users.createTelegramLink(token, user.id);
      setLink(result.url);
      setCopied(false);
    });

  const handleTest = () =>
    run(async () => {
      setTestResult(await api.users.sendTelegramTest(token, user.id));
    });

  const handleUnlink = () =>
    run(async () => {
      await api.users.unlinkTelegram(token, user.id);
      setLink(null);
      await onChanged();
    });

  async function handleCopy() {
    if (!link) return;
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
    } catch {
      // Clipboard refused (permissions): the link is selectable in the box anyway.
    }
  }

  const secondaryButton =
    'rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:border-ink disabled:opacity-50';

  return (
    <div className="space-y-2 rounded border-t border-border bg-bg p-3">
      <p className="text-xs text-ink-soft">{t('userManagement.telegramExplain', { name: user.displayName })}</p>
      <p className={`text-sm ${user.telegramLinked ? 'text-success' : 'text-ink-soft'}`}>
        {user.telegramLinked ? t('userManagement.telegramStatusLinked') : t('userManagement.telegramStatusNotLinked')}
      </p>

      {link && (
        <div className="flex flex-wrap gap-2">
          <input
            readOnly
            dir="ltr"
            aria-label={t('userManagement.telegram')}
            value={link}
            onFocus={(e) => e.target.select()}
            className="min-w-0 flex-1 rounded border border-border bg-panel px-3 py-1.5 font-mono text-xs text-ink focus:border-accent focus:outline-none"
          />
          <button type="button" onClick={handleCopy} className={secondaryButton}>
            {copied ? t('userManagement.telegramCopied') : t('userManagement.telegramCopy')}
          </button>
        </div>
      )}

      {error && <p className="text-sm text-danger">{error}</p>}
      {testResult && (
        <p className={`text-sm ${testResult.success ? 'text-success' : 'text-danger'}`} dir="auto">
          {testResult.success
            ? t('userManagement.telegramTestSent')
            : `${t('userManagement.phoneTestFailed')} ${testResult.error ?? ''}`}
        </p>
      )}

      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={handleCreateLink}
          disabled={busy}
          className="rounded bg-accent px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
        >
          {t('userManagement.telegramCreateLink')}
        </button>
        {user.telegramLinked && (
          <>
            <button type="button" onClick={handleTest} disabled={busy} className={secondaryButton}>
              {busy ? t('userManagement.phoneTesting') : t('userManagement.phoneTest')}
            </button>
            <button
              type="button"
              onClick={handleUnlink}
              disabled={busy}
              className="rounded border border-danger bg-panel px-3 py-1.5 text-sm text-danger hover:bg-bg disabled:opacity-50"
            >
              {t('userManagement.telegramUnlink')}
            </button>
          </>
        )}
        <button type="button" onClick={onClose} className="px-3 py-1.5 text-sm text-ink-soft">
          {t('userManagement.close')}
        </button>
      </div>
    </div>
  );
}
