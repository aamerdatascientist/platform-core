import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { TelegramStatusDto } from '../types';

/**
 * Settings > Reminders: the signed-in user connects their own Telegram. "Connect" asks the
 * server for a one-time link and opens it; Telegram shows the bot with a Start button, and
 * pressing it links the chat. Hidden entirely while the server has no bot.
 *
 * The link is fetched on click, not up front - fetching it on page load would replace a link
 * an administrator had just sent this person. To keep the new tab from being blocked as a
 * pop-up, the tab is opened empty inside the click and pointed at the link once it arrives.
 */
export function TelegramConnect({
  token,
  sectionClass,
  headingClass,
}: {
  token: string;
  sectionClass: string;
  headingClass: string;
}) {
  const { t } = useTranslation();
  const [status, setStatus] = useState<TelegramStatusDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const [error, setError] = useErrorMessage();

  const refresh = useCallback(async () => {
    try {
      const next = await api.me.telegramStatus(token);
      setStatus(next);
      if (next.linked) setWaiting(false);
    } catch {
      setStatus(null);
    }
  }, [token]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  // Coming back from Telegram: check whether Start was pressed.
  useEffect(() => {
    if (!waiting) return;
    const onVisible = () => {
      if (document.visibilityState === 'visible') refresh();
    };
    document.addEventListener('visibilitychange', onVisible);
    window.addEventListener('focus', onVisible);
    return () => {
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('focus', onVisible);
    };
  }, [waiting, refresh]);

  async function handleConnect() {
    setError(null);
    setBusy(true);
    const tab = window.open('', '_blank');
    try {
      const link = await api.me.createTelegramLink(token);
      if (tab) tab.location.href = link.url;
      else window.location.href = link.url;
      setWaiting(true);
    } catch (err) {
      tab?.close();
      setError({ err, fallbackKey: 'settings.telegramError' });
    } finally {
      setBusy(false);
    }
  }

  async function handleDisconnect() {
    setError(null);
    setBusy(true);
    try {
      await api.me.unlinkTelegram(token);
      await refresh();
    } catch (err) {
      setError({ err, fallbackKey: 'settings.telegramError' });
    } finally {
      setBusy(false);
    }
  }

  if (!status?.configured) return null;

  const buttonClass = 'rounded px-4 py-2 text-sm font-medium disabled:opacity-50';

  return (
    <section className={sectionClass} aria-labelledby="settings-notifications">
      <h3 id="settings-notifications" className={headingClass}>
        {t('settings.notifications')}
      </h3>
      <div className="space-y-3">
        <div className="flex items-center justify-between gap-4">
          <span className="text-sm text-ink">{t('settings.telegram')}</span>
          <span className={`text-sm ${status.linked ? 'text-success' : 'text-ink-soft'}`}>
            {status.linked ? t('settings.telegramConnected') : t('settings.telegramNotConnected')}
          </span>
        </div>
        {!status.linked && <p className="text-xs text-ink-soft">{t('settings.telegramExplain')}</p>}
        {waiting && !status.linked && <p className="text-sm text-ink">{t('settings.telegramWaiting')}</p>}
        {error && <p className="text-sm text-danger">{error}</p>}
        <div className="flex flex-wrap gap-2">
          {status.linked ? (
            <>
              <button
                type="button"
                onClick={handleConnect}
                disabled={busy}
                className={`${buttonClass} border border-border bg-panel text-ink hover:border-ink`}
              >
                {t('settings.telegramReconnect')}
              </button>
              <button
                type="button"
                onClick={handleDisconnect}
                disabled={busy}
                className={`${buttonClass} border border-danger bg-panel text-danger hover:bg-bg`}
              >
                {t('settings.telegramDisconnect')}
              </button>
            </>
          ) : (
            <>
              <button type="button" onClick={handleConnect} disabled={busy} className={`${buttonClass} bg-accent text-accent-ink`}>
                {busy ? t('settings.telegramOpening') : t('settings.telegramConnect')}
              </button>
              {waiting && (
                <button
                  type="button"
                  onClick={refresh}
                  className={`${buttonClass} border border-border bg-panel text-ink hover:border-ink`}
                >
                  {t('settings.telegramCheck')}
                </button>
              )}
            </>
          )}
        </div>
      </div>
    </section>
  );
}
