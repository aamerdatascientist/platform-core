import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import ar from './locales/ar.json';
import en from './locales/en.json';

export const LANGUAGE_STORAGE_KEY = 'lang';
export const RTL_LANGUAGES = ['ar'];

export function isRtl(language: string) {
  return RTL_LANGUAGES.includes(language);
}

function applyDirection(language: string) {
  document.documentElement.dir = isRtl(language) ? 'rtl' : 'ltr';
  document.documentElement.lang = language;
}

const storedLanguage = localStorage.getItem(LANGUAGE_STORAGE_KEY);

i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ar: { translation: ar },
  },
  lng: storedLanguage ?? 'en',
  fallbackLng: 'en',
  interpolation: {
    escapeValue: false,
  },
});

// Keep <html dir/lang> in sync with the active language - this is what makes every
// Tailwind rtl:/ltr: variant and logical-property utility (ms-, me-, start-, end-, ...)
// throughout the app respond to a language switch, not just the sidebar.
applyDirection(i18n.language);
i18n.on('languageChanged', (language) => {
  localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
  applyDirection(language);
});

export default i18n;
