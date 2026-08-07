/**
 * Deliberately NOT React state - the API client (client.ts) needs to read and update
 * tokens during a silent refresh-and-retry, which happens outside any component's render
 * cycle. Components that need to react to sign-in/sign-out subscribe via `subscribe()`
 * instead of importing React state from here.
 */
export interface StoredTokens {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
}

const STORAGE_KEY = 'platform_auth_tokens';
type Listener = (tokens: StoredTokens | null) => void;
let listeners: Listener[] = [];

export function getTokens(): StoredTokens | null {
  const raw = sessionStorage.getItem(STORAGE_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as StoredTokens;
  } catch {
    return null;
  }
}

export function setTokens(tokens: StoredTokens | null): void {
  if (tokens) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tokens));
  else sessionStorage.removeItem(STORAGE_KEY);
  listeners.forEach((listener) => listener(tokens));
}

/** Returns an unsubscribe function - call it from a useEffect cleanup. */
export function subscribe(listener: Listener): () => void {
  listeners.push(listener);
  return () => {
    listeners = listeners.filter((l) => l !== listener);
  };
}
