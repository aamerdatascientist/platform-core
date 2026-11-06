export const MODE_STORAGE_KEY = 'mode';

/** What is on screen. */
export type Mode = 'dark' | 'light';
/** What the person chose; 'system' follows the phone/computer setting live ("Match phone"). */
export type ModePreference = Mode | 'system';

const query = typeof window !== 'undefined' && window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

function readPreference(): ModePreference {
  try {
    const stored = localStorage.getItem(MODE_STORAGE_KEY);
    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
  } catch {
    return 'system';
  }
}

function resolve(preference: ModePreference): Mode {
  if (preference !== 'system') return preference;
  return query?.matches ? 'dark' : 'light';
}

let currentPreference: ModePreference = readPreference();
let currentMode: Mode = resolve(currentPreference);

type Listener = (mode: Mode) => void;
const listeners = new Set<Listener>();

function apply(mode: Mode) {
  if (mode === currentMode && document.documentElement.getAttribute('data-mode') === mode) return;
  currentMode = mode;
  document.documentElement.setAttribute('data-mode', mode);
  listeners.forEach((listener) => listener(mode));
}

// "Match phone" follows the system setting while the app is open.
query?.addEventListener?.('change', () => {
  if (currentPreference === 'system') apply(resolve('system'));
});

/**
 * A plain module singleton with a localStorage-backed value and a subscribe callback (same
 * pattern as src/i18n/index.ts). public/boot.js already set data-mode before React mounted,
 * from the same key and the same rule, so nothing is applied on load here.
 */
export function getMode(): Mode {
  return currentMode;
}

export function getModePreference(): ModePreference {
  return currentPreference;
}

export function setModePreference(preference: ModePreference) {
  currentPreference = preference;
  try {
    localStorage.setItem(MODE_STORAGE_KEY, preference);
  } catch {
    // Private mode: the choice lasts until reload.
  }
  apply(resolve(preference));
}

/** An explicit light or dark choice. */
export function setMode(mode: Mode) {
  setModePreference(mode);
}

export function toggleMode() {
  setMode(currentMode === 'dark' ? 'light' : 'dark');
}

/** Returns an unsubscribe function - same shape as i18n.on(...)'s usage in this app. */
export function onModeChanged(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
