import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useWhatsAppGate } from '../config/whatsapp';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { TelegramStatusDto } from '../types';
import { Icon } from './icons';
import { Button, Card } from './ui';

/**
 * Settings > Reminders: the signed-in user connects their own Telegram. "Connect" asks the
 * server for a one-time link and opens it; Telegram shows the bot with a Start button, and
 * pressing it links the chat. Hidden entirely while the server has no bot.
 *
 * The link is fetched on click, not up front - fetching it on page load would replace a link
 * an administrator had just sent this person. To keep the new tab from being blocked as a
 * pop-up, the tab is opened empty inside the click and pointed at the link once it arrives.
 */
export function TelegramConnect({ token }: { token: string }) {
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

  return (
    <section className="flex flex-col gap-2" aria-labelledby="settings-notifications">
      <h2 id="settings-notifications" className="px-1 text-[14px] font-bold text-ink-soft">
        {t('settings.notifications')}
      </h2>
      <Card kind="phone-secondary" className="flex flex-col divide-y divide-divider px-4">
      {status?.configured && (
      <div className="flex flex-col gap-3 py-[14px]">
        <div className="flex items-center gap-3">
          <span aria-hidden="true" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-ui-lg bg-accent-soft text-accent">
            <Icon name="send" size={20} />
          </span>
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <span className="text-[16px] font-bold text-ink">{t('settings.telegram')}</span>
            <span className={`text-[14px] ${status.linked ? 'text-success' : 'text-ink-soft'}`}>
              {status.linked ? t('settingsV2.telegramConnectedShort') : t('settings.telegramNotConnected')}
            </span>
          </div>
          <Button variant="secondary" size="compact" onClick={handleConnect} disabled={busy} className="!rounded-ui-lg !text-[14px]">
            {busy ? t('settings.telegramOpening') : status.linked ? t('settings.telegramReconnect') : t('settings.telegramConnect')}
          </Button>
        </div>
        {!status.linked && <p className="text-[13px] text-ink-soft">{t('settings.telegramExplain')}</p>}
        {waiting && !status.linked && (
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-[14px] text-ink">{t('settings.telegramWaiting')}</p>
            <Button variant="link" onClick={refresh}>{t('settings.telegramCheck')}</Button>
          </div>
        )}
        {status.linked && (
          <Button variant="link" onClick={handleDisconnect} disabled={busy} className="self-start !px-0 !text-danger">
            {t('settings.telegramDisconnect')}
          </Button>
        )}
        {error && <p className="text-[14px] text-danger">{error}</p>}
      </div>
      )}
        <WhatsAppRow />
      </Card>
    </section>
  );
}

/** WhatsApp, shown as it will be once connected; until then "Connect" only says it isn't set up yet. */
function WhatsAppRow() {
  const { t } = useTranslation();
  const { notice, guard } = useWhatsAppGate();
  return (
    <div className="flex flex-col gap-3 py-[14px]">
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-ui-lg bg-success-soft text-success">
          <Icon name="bell" size={20} />
        </span>
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="text-[16px] font-bold text-ink">{t('whatsapp.title')}</span>
          <span className="text-[14px] text-ink-soft">{t('whatsapp.notConnected')}</span>
        </div>
        <Button variant="secondary" size="compact" onClick={() => guard(() => undefined)} className="!rounded-ui-lg !text-[14px]">
          {t('whatsapp.connect')}
        </Button>
      </div>
      <p className="text-[13px] text-ink-soft">{t('whatsapp.explain')}</p>
      {notice && (
        <p role="status" className="rounded-ui-lg bg-warning-soft px-3 py-2 text-[14px] text-warning-strong">
          {t('whatsapp.notReady')}
        </p>
      )}
    </div>
  );
}
