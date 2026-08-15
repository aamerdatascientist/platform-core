import { useTranslation } from 'react-i18next';

interface LanguageToggleProps {
  /** 'dark' (default) is the sidebar's own dark background; 'light' is for use on
   *  light/white backgrounds, e.g. the sign-in page. */
  tone?: 'dark' | 'light';
}

const TONE_CLASSES = {
  dark: { active: 'font-medium text-white', inactive: 'text-sidebar-muted hover:text-white', divider: 'text-sidebar-border' },
  light: { active: 'font-medium text-ink', inactive: 'text-ink-muted hover:text-ink', divider: 'text-line' },
};

/**
 * Always shows both language options ("EN / عربي") with the active one highlighted,
 * rather than a single label that changes - makes the current language and the fact
 * that it's a toggle both obvious at a glance. Shared i18n instance means switching
 * here or from the sidebar's copy of this component stays in sync everywhere.
 */
export function LanguageToggle({ tone = 'dark' }: LanguageToggleProps) {
  const { i18n } = useTranslation();
  const isArabic = i18n.language === 'ar';
  const classes = TONE_CLASSES[tone];

  function toggle() {
    i18n.changeLanguage(isArabic ? 'en' : 'ar');
  }

  return (
    <button
      onClick={toggle}
      className="flex items-center gap-1 text-[11px] uppercase tracking-wide"
      aria-label="Toggle language"
    >
      <span className={isArabic ? classes.inactive : classes.active}>EN</span>
      <span className={classes.divider}>/</span>
      <span className={isArabic ? classes.active : classes.inactive}>عربي</span>
    </button>
  );
}
