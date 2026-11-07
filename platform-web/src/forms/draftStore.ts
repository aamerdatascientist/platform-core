/**
 * Form drafts (redesign HANDOFF 03-06): the answers autosave on this device so a half-filled
 * form survives closing it, a reload or a dead battery. Answers and the current step go to
 * localStorage under `draft:{userId}:{formId}`; picked photos go to IndexedDB (they are too big
 * for localStorage), under the same key. Both are cleared after a successful send. Storage can
 * be blocked (private mode, full disk), so every call tolerates failure - a draft is a
 * convenience, never something the form depends on.
 */
export interface FormDraft {
  /** Published version the answers were given against; a draft for another version is dropped. */
  versionId: string;
  values: Record<string, string>;
  step: number;
  savedAt: string;
}

const key = (userId: string, formId: string) => `draft:${userId}:${formId}`;

export function loadDraft(userId: string | null, formId: string, versionId: string): FormDraft | null {
  if (!userId) return null;
  try {
    const draft = JSON.parse(localStorage.getItem(key(userId, formId)) ?? 'null') as FormDraft | null;
    if (!draft || draft.versionId !== versionId || typeof draft.values !== 'object') return null;
    return draft;
  } catch {
    return null;
  }
}

export function saveDraft(userId: string | null, formId: string, draft: FormDraft): boolean {
  if (!userId) return false;
  try {
    localStorage.setItem(key(userId, formId), JSON.stringify(draft));
    return true;
  } catch {
    return false;
  }
}

export function clearDraft(userId: string | null, formId: string) {
  if (!userId) return;
  try {
    localStorage.removeItem(key(userId, formId));
  } catch {
    // nothing to clear
  }
  void withStore('readwrite', (store) => store.delete(key(userId, formId)));
}

// ---------------------------------------------------------------- photos (IndexedDB)

const DB = 'asas-drafts';
const STORE = 'files';

function withStore<T>(mode: IDBTransactionMode, run: (store: IDBObjectStore) => IDBRequest<T>): Promise<T | undefined> {
  return new Promise((resolve) => {
    try {
      const open = indexedDB.open(DB, 1);
      open.onupgradeneeded = () => open.result.createObjectStore(STORE);
      open.onerror = () => resolve(undefined);
      open.onsuccess = () => {
        try {
          const tx = open.result.transaction(STORE, mode);
          const req = run(tx.objectStore(STORE));
          req.onsuccess = () => resolve(req.result);
          req.onerror = () => resolve(undefined);
          tx.oncomplete = () => open.result.close();
        } catch {
          resolve(undefined);
        }
      };
    } catch {
      resolve(undefined);
    }
  });
}

/** Files are stored as-is (File survives IndexedDB's structured clone). */
export function saveDraftFiles(userId: string | null, formId: string, files: Record<string, File[]>) {
  if (!userId) return Promise.resolve(undefined);
  return withStore('readwrite', (store) => store.put(files, key(userId, formId)));
}

export async function loadDraftFiles(userId: string | null, formId: string): Promise<Record<string, File[]>> {
  if (!userId) return {};
  const files = await withStore<Record<string, File[]>>('readonly', (store) => store.get(key(userId, formId)));
  return files && typeof files === 'object' ? files : {};
}
