import { useTranslation } from 'react-i18next';

interface LanguageToggleProps {
  /** 'dark' (default) is the sidebar's own dark background; 'light' is for use on
   *  light/white backgrounds, e.g. the sign-in page. */
  tone?: 'dark' | 'light';
}

const TONE_CLASSES = {
  dark: { active: 'font-medium text-white', inactive: 'text-sidebar-muted', track: 'bg-white/20' },
  light: { active: 'font-medium text-ink', inactive: 'text-ink-muted', track: 'bg-ink/15' },
};

/**
 * iOS-style pill switch. Deliberately does NOT use logical (rtl-aware) positioning
 * anywhere in here, unlike the rest of this app's RTL work - "EN" always sits on the
 * physical left and "عربي" always on the physical right, and the knob always slides
 * left-to-right for Arabic, right-to-left for English, regardless of the active
 * direction. A language switcher needs to look and behave the same way no matter which
 * language is currently selected, or it stops being findable/usable as a control.
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
      className="flex items-center gap-2 text-[11px] uppercase tracking-wide"
      aria-label="Toggle language"
    >
      <span className={isArabic ? classes.inactive : classes.active}>EN</span>
      <span className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full ${classes.track}`}>
        <span
          className={`absolute left-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform duration-200 ${
            isArabic ? 'translate-x-4' : 'translate-x-0'
          }`}
        />
      </span>
      <span className={isArabic ? classes.active : classes.inactive}>عربي</span>
    </button>
  );
}
