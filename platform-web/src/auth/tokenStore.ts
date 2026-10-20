/**
 * Deliberately NOT React state - the API client (client.ts) needs to read and update
 * tokens during a silent refresh-and-retry, which happens outside any component's render
 * cycle. Components that need to react to sign-in/sign-out subscribe via `subscribe()`
 * instead of importing React state from here.
 *
 * Kept in localStorage (since 2026-10-20; it was sessionStorage): a project manager on a
 * phone stays signed in after closing the tab or the browser, instead of typing the password
 * for every report. Signing out (or a password change/reset, which revokes the refresh token)
 * still ends the session. Every open tab shares the one sign-in: a sign-in, sign-out or token
 * refresh in one tab reaches the others through the browser's `storage` event.
 */
export interface StoredTokens {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
}

const STORAGE_KEY = 'platform_auth_tokens';
type Listener = (tokens: StoredTokens | null) => void;
let listeners: Listener[] = [];

function storage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null; // storage blocked (some private modes) - the app still works, just not remembered
  }
}

function parse(raw: string | null): StoredTokens | null {
  if (!raw) return null;
  try {
    return JSON.parse(raw) as StoredTokens;
  } catch {
    return null;
  }
}

// Whoever was signed in before this change kept the tokens in sessionStorage - carry them
// over once, so the update doesn't sign everyone out.
try {
  const legacy = window.sessionStorage.getItem(STORAGE_KEY);
  if (legacy) {
    if (!storage()?.getItem(STORAGE_KEY)) storage()?.setItem(STORAGE_KEY, legacy);
    window.sessionStorage.removeItem(STORAGE_KEY);
  }
} catch {
  // nothing to migrate
}

export function getTokens(): StoredTokens | null {
  try {
    return parse(storage()?.getItem(STORAGE_KEY) ?? null);
  } catch {
    return null;
  }
}

export function setTokens(tokens: StoredTokens | null): void {
  try {
    if (tokens) storage()?.setItem(STORAGE_KEY, JSON.stringify(tokens));
    else storage()?.removeItem(STORAGE_KEY);
  } catch {
    // storage full or blocked - listeners below still update this tab
  }
  listeners.forEach((listener) => listener(tokens));
}

/** Returns an unsubscribe function - call it from a useEffect cleanup. */
export function subscribe(listener: Listener): () => void {
  listeners.push(listener);
  return () => {
    listeners = listeners.filter((l) => l !== listener);
  };
}

// Another tab signed in, out, or refreshed the tokens.
window.addEventListener('storage', (event) => {
  if (event.key !== STORAGE_KEY && event.key !== null) return;
  const tokens = parse(event.newValue);
  listeners.forEach((listener) => listener(tokens));
});
