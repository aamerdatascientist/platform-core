import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getMode, onModeChanged, toggleMode } from '../theme/mode';
import { Switch } from './Switch';

interface ModeToggleProps {
  tone?: 'dark' | 'light';
}

/**
 * State comes from src/theme/mode.ts (a plain module singleton with a subscribe
 * callback, same pattern as i18n.on(...) - not React Context), not local component
 * state, so every mounted instance (sidebar + sign-in) stays in sync.
 */
export function ModeToggle({ tone = 'dark' }: ModeToggleProps) {
  const { t } = useTranslation();
  const [mode, setModeState] = useState(getMode());

  useEffect(() => onModeChanged(setModeState), []);

  return (
    <Switch
      checked={mode === 'dark'}
      onChange={toggleMode}
      leftLabel="☀"
      rightLabel="☾"
      tone={tone}
      ariaLabel={t('common.toggleMode')}
    />
  );
}
