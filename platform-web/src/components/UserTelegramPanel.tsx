import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { UserSummaryDto } from '../types';

/**
 * A user's Telegram connection (Users page). The usual way (2026-11-05, owner's choice): the
 * administrator makes a small Telegram group with the engineer, presses "Link to a group" and
 * picks it - the bot joins and the engineer's reminders go there, with nothing for the engineer
 * to do. A private-chat link to send the person is the alternative. Also: test, disconnect.
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
  const [privateLink, setPrivateLink] = useState<string | null>(null);
  const [groupLink, setGroupLink] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
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

  /** Opens Telegram's "add to group" screen. The tab is opened inside the click (pop-up blockers). */
  async function handleLinkGroup() {
    const tab = window.open('', '_blank');
    await run(async () => {
      try {
        const result = await api.users.createTelegramLink(token, user.id);
        setGroupLink(result.groupUrl);
        setPrivateLink(null);
        if (tab) tab.location.href = result.groupUrl;
      } catch (err) {
        tab?.close();
        throw err;
      }
    });
  }

  const handlePrivateLink = () =>
    run(async () => {
      const result = await api.users.createTelegramLink(token, user.id);
      setPrivateLink(result.url);
      setGroupLink(null);
    });

  const handleRefresh = () => run(async () => onChanged());

  const handleTest = () =>
    run(async () => {
      setTestResult(await api.users.sendTelegramTest(token, user.id));
    });

  const handleUnlink = () =>
    run(async () => {
      await api.users.unlinkTelegram(token, user.id);
      setPrivateLink(null);
      setGroupLink(null);
      await onChanged();
    });

  async function handleCopy(link: string) {
    try {
      await navigator.clipboard.writeText(link);
      setCopied(link);
    } catch {
      // Clipboard refused (permissions): the link is selectable in the box anyway.
    }
  }

  const secondaryButton =
    'rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:border-ink disabled:opacity-50';

  function linkBox(link: string) {
    return (
      <div className="flex flex-wrap gap-2">
        <input
          readOnly
          dir="ltr"
          aria-label={t('userManagement.telegram')}
          value={link}
          onFocus={(e) => e.target.select()}
          className="min-w-0 flex-1 rounded border border-border bg-panel px-3 py-1.5 font-mono text-xs text-ink focus:border-accent focus:outline-none"
        />
        <button type="button" onClick={() => handleCopy(link)} className={secondaryButton}>
          {copied === link ? t('userManagement.telegramCopied') : t('userManagement.telegramCopy')}
        </button>
      </div>
    );
  }

  const status = !user.telegramLinked
    ? t('userManagement.telegramStatusNotLinked')
    : user.telegramIsGroup
      ? t('userManagement.telegramStatusGroup')
      : t('userManagement.telegramStatusPrivate');

  return (
    <div className="space-y-2 rounded border-t border-border bg-bg p-3">
      <p className={`text-sm ${user.telegramLinked ? 'text-success' : 'text-ink-soft'}`}>{status}</p>

      {!user.telegramLinked && (
        <div className="space-y-1 text-xs text-ink-soft">
          <p>{t('userManagement.telegramExplain', { name: user.displayName })}</p>
          <p>{t('userManagement.telegramStep1', { name: user.displayName })}</p>
          <p>{t('userManagement.telegramStep2')}</p>
        </div>
      )}

      {groupLink && !user.telegramLinked && (
        <>
          <p className="text-sm text-ink">{t('userManagement.telegramGroupWaiting')}</p>
          {linkBox(groupLink)}
        </>
      )}

      {privateLink && (
        <>
          <p className="text-xs text-ink-soft">{t('userManagement.telegramPrivateExplain', { name: user.displayName })}</p>
          {linkBox(privateLink)}
        </>
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
        {user.telegramLinked ? (
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
        ) : (
          <>
            <button
              type="button"
              onClick={handleLinkGroup}
              disabled={busy}
              className="rounded bg-accent px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
            >
              {t('userManagement.telegramLinkGroup')}
            </button>
            {groupLink && (
              <button type="button" onClick={handleRefresh} disabled={busy} className={secondaryButton}>
                {t('userManagement.telegramRefresh')}
              </button>
            )}
            <button type="button" onClick={handlePrivateLink} disabled={busy} className={secondaryButton}>
              {t('userManagement.telegramPrivateLink')}
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
