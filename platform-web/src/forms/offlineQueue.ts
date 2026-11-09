/**
 * The offline queue (redesign HANDOFF §9.6, screen 12): an entry sent with no connection - or
 * whose request never got an answer - is kept on this phone in IndexedDB, photos included,
 * and sent later by QueueProvider. Each entry carries its idempotency key (the server saves it
 * only once, however many times it is retried) and the time Send was pressed (the server uses
 * it for the assignment day; owner decision 8). Storage can be blocked; every call tolerates it.
 */
export interface QueuedEntry {
  /** Idempotency key - also this entry's id here. */
  key: string;
  userId: string;
  formId: string;
  formName: string;
  /** "Demo project", for the waiting list. */
  project: string | null;
  values: Record<string, unknown>;
  files: { fieldCode: string; file: File }[];
  /** When Send was pressed (ISO). */
  enteredAtUtc: string;
  /** Set when the server refused it (e.g. older than 3 days); it then waits for the person. */
  error: string | null;
}

const DB = 'asas-queue';
const STORE = 'entries';

function open(): Promise<IDBDatabase | null> {
  return new Promise((resolve) => {
    try {
      const req = indexedDB.open(DB, 1);
      req.onupgradeneeded = () => req.result.createObjectStore(STORE, { keyPath: 'key' });
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => resolve(null);
    } catch {
      resolve(null);
    }
  });
}

async function run<T>(mode: IDBTransactionMode, op: (store: IDBObjectStore) => IDBRequest<T>): Promise<T | undefined> {
  const db = await open();
  if (!db) return undefined;
  return new Promise((resolve) => {
    try {
      const tx = db.transaction(STORE, mode);
      const req = op(tx.objectStore(STORE));
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => resolve(undefined);
      tx.oncomplete = () => db.close();
    } catch {
      resolve(undefined);
    }
  });
}

/** Keeps an entry; false if this phone can't store it (then it isn't saved anywhere). */
export async function enqueue(entry: QueuedEntry): Promise<boolean> {
  return (await run('readwrite', (s) => s.put(entry))) !== undefined;
}

export async function queuedEntries(userId: string | null): Promise<QueuedEntry[]> {
  if (!userId) return [];
  const all = (await run<QueuedEntry[]>('readonly', (s) => s.getAll())) ?? [];
  return all.filter((e) => e.userId === userId).sort((a, b) => a.enteredAtUtc.localeCompare(b.enteredAtUtc));
}

export async function dequeue(key: string) {
  await run('readwrite', (s) => s.delete(key));
}

export async function markFailed(entry: QueuedEntry, error: string) {
  await run('readwrite', (s) => s.put({ ...entry, error }));
}

/** A fresh idempotency key (36 chars, matches the server's [A-Za-z0-9_-]{8,64}). */
export function newKey() {
  return typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
}
