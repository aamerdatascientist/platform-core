import { useEffect, useState } from 'react';
import { getMode, onModeChanged, toggleMode } from '../theme/mode';

interface ModeToggleProps {
  /** Same meaning as LanguageToggle's tone prop - 'dark' for the sidebar surface,
   *  'light' for a panel/page background (e.g. the sign-in card). */
  tone?: 'dark' | 'light';
}

const TONE_CLASSES = {
  dark: { active: 'font-medium text-sidebar-ink-strong', inactive: 'text-sidebar-ink', track: 'bg-sidebar-ink/20' },
  light: { active: 'font-medium text-ink', inactive: 'text-ink-soft', track: 'bg-ink/15' },
};

/**
 * Mirrors LanguageToggle's iOS-style pill switch exactly, including the same
 * dir="ltr" fix - a control like this needs to look and behave the same way
 * regardless of which language direction is currently active, or it stops being
 * findable/usable. State comes from src/theme/mode.ts (a plain module singleton with
 * a subscribe callback, same pattern as i18n.on(...) - not React Context), not local
 * component state, so every mounted instance (sidebar + sign-in) stays in sync.
 */
export function ModeToggle({ tone = 'dark' }: ModeToggleProps) {
  const [mode, setModeState] = useState(getMode());
  const isDark = mode === 'dark';
  const classes = TONE_CLASSES[tone];

  useEffect(() => onModeChanged(setModeState), []);

  return (
    <button
      onClick={toggleMode}
      dir="ltr"
      className="flex items-center gap-2.5 text-xs uppercase tracking-wide"
      aria-label="Toggle light/dark mode"
    >
      <span className={isDark ? classes.inactive : classes.active} aria-hidden="true">
        ☀
      </span>
      <span className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full ${classes.track}`}>
        <span
          className={`absolute left-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform duration-200 ${
            isDark ? 'translate-x-5' : 'translate-x-0'
          }`}
        />
      </span>
      <span className={isDark ? classes.active : classes.inactive} aria-hidden="true">
        ☾
      </span>
    </button>
  );
}
