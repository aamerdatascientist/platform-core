export const MODE_STORAGE_KEY = 'mode';
export type Mode = 'dark' | 'light';

const DEFAULT_MODE: Mode = 'dark';

function applyMode(mode: Mode) {
  document.documentElement.setAttribute('data-mode', mode);
}

let currentMode: Mode = (localStorage.getItem(MODE_STORAGE_KEY) as Mode | null) ?? DEFAULT_MODE;

type Listener = (mode: Mode) => void;
const listeners = new Set<Listener>();

/**
 * Mirrors src/i18n/index.ts's pattern exactly - a plain module singleton with a
 * localStorage-backed value and a subscribe callback, not React Context. index.html's
 * pre-mount script already set data-mode from the same localStorage key before React
 * mounted (avoids a flash of the wrong mode on load), so this module doesn't need to
 * apply it again on load - only on future changes.
 */
export function getMode(): Mode {
  return currentMode;
}

export function setMode(mode: Mode) {
  if (mode === currentMode) return;
  currentMode = mode;
  localStorage.setItem(MODE_STORAGE_KEY, mode);
  applyMode(mode);
  listeners.forEach((listener) => listener(mode));
}

export function toggleMode() {
  setMode(currentMode === 'dark' ? 'light' : 'dark');
}

/** Returns an unsubscribe function - same shape as i18n.on(...)'s usage in this app. */
export function onModeChanged(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
