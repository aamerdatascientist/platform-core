import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { FULL_SCREEN_ATTRIBUTE } from './shell/fullScreen';
import { useOfflineQueue } from './shell/OfflineQueue';
import { Icon } from './icons';
import { Button, Card } from './ui';

/**
 * "Waiting to send (n)" (12): each entry kept on this phone - form, project, photo count, time.
 * An entry the server refused shows why, with Remove.
 */
export function WaitingList() {
  const { t, i18n } = useTranslation();
  const queue = useOfflineQueue();
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  if (!queue || queue.entries.length === 0) return null;
  const time = (iso: string) => new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
  return (
    <Card kind="phone-secondary" className="flex w-full flex-col gap-3 px-4 py-[14px] text-start">
      <span className="text-[13px] font-bold text-ink-soft">{t('offline.waiting', { count: queue.entries.length })}</span>
      {queue.entries.map((e) => (
        <div key={e.key} className="flex items-center gap-3">
          <span aria-hidden="true" className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-ui-lg ${e.error ? 'bg-danger-soft text-danger' : 'bg-accent-soft text-accent'}`}>
            <Icon name={e.error ? 'alert' : 'clipboard'} size={20} />
          </span>
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <span className="truncate text-[16px] font-bold text-ink">
              <bdi>{e.formName}</bdi>
            </span>
            <span className="truncate text-[14px] text-ink-soft">
              {[e.project, t('sent.photos', { count: e.files.length }), time(e.enteredAtUtc)].filter(Boolean).join(' · ')}
            </span>
            {e.error && <span className="text-[13px] text-danger">{e.error}</span>}
          </div>
          {e.error && (
            <Button size="compact" variant="link" className="!text-danger" onClick={() => void queue.discard(e.key)}>
              {t('common.remove')}
            </Button>
          )}
        </div>
      ))}
    </Card>
  );
}

/**
 * Saved on this phone (screen 12): shown instead of Sent (07) when the entry couldn't be sent
 * now. A clock badge in warning colours, "Saved on this phone", what happens next, the waiting
 * list, and Back to Today / Try sending now. The offline banner sits above it (Layout).
 */
export function OfflineSavedScreen() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queue = useOfflineQueue();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    window.scrollTo(0, 0);
    ref.current?.focus();
    document.documentElement.setAttribute(FULL_SCREEN_ATTRIBUTE, '');
    return () => document.documentElement.removeAttribute(FULL_SCREEN_ATTRIBUTE);
  }, []);

  return (
    <div ref={ref} role="status" tabIndex={-1} className="mx-auto flex min-h-[calc(100dvh-40px)] max-w-[560px] flex-col outline-none lg:min-h-0" style={{ paddingBottom: 'calc(28px + env(safe-area-inset-bottom))' }}>
      <div className="flex flex-1 flex-col items-center justify-center gap-[18px] py-6 text-center">
        <span aria-hidden="true" className="flex h-24 w-24 items-center justify-center rounded-full bg-warning-soft">
          <span className="flex h-16 w-16 items-center justify-center rounded-full bg-warning text-bg">
            <Icon name="clock" size={30} />
          </span>
        </span>
        <div className="flex flex-col gap-1.5">
          <h1 className="text-[26px] font-extrabold text-ink">{t('offline.title')}</h1>
          <p className="text-[16px] leading-[1.55] text-ink-soft">{t('offline.body')}</p>
        </div>
        <WaitingList />
      </div>
      <div className="flex flex-col gap-[10px]">
        <Button block onClick={() => navigate('/')}>
          {t('sent.backToToday')}
        </Button>
        <Button block variant="secondary" className="!min-h-[52px] !text-[17px]" disabled={queue?.sending} onClick={() => void queue?.sendNow()}>
          {t('offline.trySend')}
        </Button>
      </div>
    </div>
  );
}
