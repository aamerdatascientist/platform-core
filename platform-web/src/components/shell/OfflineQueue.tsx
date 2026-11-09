import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from '../../i18n';
import { ApiError, api } from '../../api/client';
import { dequeue, enqueue, markFailed, queuedEntries } from '../../forms/offlineQueue';
import type { QueuedEntry } from '../../forms/offlineQueue';
import { Icon } from '../icons';
import { useOptionalShell } from './ShellData';

const RETRY_EVERY_MS = 60_000;

interface QueueState {
  entries: QueuedEntry[];
  offline: boolean;
  sending: boolean;
  /** Keep an entry on this phone; false when the phone can't store it. */
  add: (entry: QueuedEntry) => Promise<boolean>;
  sendNow: () => Promise<void>;
  discard: (key: string) => Promise<void>;
}

const QueueContext = createContext<QueueState | null>(null);

export function useOfflineQueue() {
  return useContext(QueueContext);
}

/** Whether the phone says it has a connection, kept current. */
export function useOnline() {
  const [online, setOnline] = useState(() => (typeof navigator === 'undefined' ? true : navigator.onLine));
  useEffect(() => {
    const up = () => setOnline(true);
    const down = () => setOnline(false);
    window.addEventListener('online', up);
    window.addEventListener('offline', down);
    return () => {
      window.removeEventListener('online', up);
      window.removeEventListener('offline', down);
    };
  }, []);
  return online;
}

/**
 * Sends what was kept on this phone (§9.6): when the app opens, when the connection comes back,
 * every 60 s while something is waiting, and on "Try sending now". Entries go one by one with
 * their idempotency key and entered-at time (the server saves each once and counts it for the
 * day it was entered); photos follow. A refusal (e.g. older than 3 days) marks that entry and
 * moves on; no answer at all means still offline, so it stops until the next try. When the
 * queue empties, a toast says "Sent: n saved entries".
 */
export function QueueProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const shell = useOptionalShell();
  const token = shell?.token ?? null;
  const userId = shell?.me?.id ?? null;
  const online = useOnline();
  const [entries, setEntries] = useState<QueuedEntry[]>([]);
  const [sending, setSending] = useState(false);
  const [toast, setToast] = useState<string | null>(null);
  const running = useRef(false);

  const reload = useCallback(async () => setEntries(await queuedEntries(userId)), [userId]);

  const sendNow = useCallback(async () => {
    if (running.current || !token || !userId) return;
    running.current = true;
    setSending(true);
    let sent = 0;
    try {
      for (const entry of await queuedEntries(userId)) {
        if (entry.error) continue;
        try {
          const { id } = await api.submissions.submit(token, entry.formId, entry.values, { idempotencyKey: entry.key, enteredAtUtc: entry.enteredAtUtc });
          const results = await Promise.allSettled(entry.files.map((f) => api.files.upload(token, entry.formId, id, f.fieldCode, f.file)));
          const left = entry.files.filter((_, i) => results[i].status === 'rejected');
          sent++;
          if (left.length === 0) await dequeue(entry.key);
          else {
            // The entry is saved; only these photos still need to go (the same key finds the same record).
            await enqueue({ ...entry, files: left });
            break;
          }
        } catch (err) {
          if (err instanceof ApiError && err.status < 500 && err.status !== 429 && err.code !== 'submission.inProgress') {
            const reason = err.code && i18n.exists(`apiErrors.${err.code}`) ? t(`apiErrors.${err.code}`) : err.message;
            await markFailed(entry, reason);
            continue;
          }
          break; // no answer, or the server is busy - try again later
        }
      }
    } finally {
      running.current = false;
      setSending(false);
      const left = await queuedEntries(userId);
      setEntries(left);
      if (sent > 0) {
        shell?.refresh();
        if (left.filter((e) => !e.error).length === 0) {
          setToast(t('offline.sentToast', { count: sent }));
          window.setTimeout(() => setToast(null), 4000);
        }
      }
    }
  }, [token, userId, t, shell]);

  // On open, and whenever the connection comes back.
  useEffect(() => {
    void reload().then(() => online && void sendNow());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId, online]);

  // Every 60 s while something is waiting.
  useEffect(() => {
    if (entries.filter((e) => !e.error).length === 0) return;
    const timer = window.setInterval(() => void sendNow(), RETRY_EVERY_MS);
    return () => window.clearInterval(timer);
  }, [entries, sendNow]);

  const value = useMemo<QueueState>(
    () => ({
      entries,
      offline: !online,
      sending,
      add: async (entry) => {
        const ok = await enqueue(entry);
        await reload();
        return ok;
      },
      sendNow,
      discard: async (key) => {
        await dequeue(key);
        await reload();
      },
    }),
    [entries, online, sending, reload, sendNow],
  );

  return (
    <QueueContext.Provider value={value}>
      {children}
      {toast && (
        <div role="status" className="fixed inset-x-4 z-50 mx-auto flex max-w-[420px] items-center gap-2 rounded-ui-xl bg-ink px-4 py-3 text-[15px] font-medium text-bg shadow-elev-1" style={{ bottom: 'calc(var(--tabbar-h, 72px) + env(safe-area-inset-bottom) + 16px)' }}>
          <Icon name="check" size={18} />
          {toast}
        </div>
      )}
    </QueueContext.Provider>
  );
}

/** "No connection. You can keep working." - full width at the top while offline (12). */
export function OfflineBanner() {
  const { t } = useTranslation();
  const online = useOnline();
  if (online) return null;
  return (
    <div role="status" className="flex items-center gap-[10px] bg-warning-soft px-5 py-3 text-[14px] font-medium text-warning-strong">
      <Icon name="wifiOff" size={18} />
      {t('offline.banner')}
    </div>
  );
}
