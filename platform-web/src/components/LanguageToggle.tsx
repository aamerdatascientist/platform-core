import { useTranslation } from 'react-i18next';

/**
 * Always shows both language options ("EN / عربي") with the active one highlighted,
 * rather than a single label that changes - makes the current language and the fact
 * that it's a toggle both obvious at a glance.
 */
export function LanguageToggle() {
  const { i18n } = useTranslation();
  const isArabic = i18n.language === 'ar';

  function toggle() {
    i18n.changeLanguage(isArabic ? 'en' : 'ar');
  }

  return (
    <button
      onClick={toggle}
      className="flex items-center gap-1 text-[11px] uppercase tracking-wide"
      aria-label="Toggle language"
    >
      <span className={isArabic ? 'text-sidebar-muted hover:text-white' : 'font-medium text-white'}>EN</span>
      <span className="text-sidebar-border">/</span>
      <span className={isArabic ? 'font-medium text-white' : 'text-sidebar-muted hover:text-white'}>عربي</span>
    </button>
  );
}
