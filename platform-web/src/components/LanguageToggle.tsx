import { useTranslation } from 'react-i18next';
import { Switch } from './Switch';

interface LanguageToggleProps {
  tone?: 'dark' | 'light';
}

export function LanguageToggle({ tone = 'dark' }: LanguageToggleProps) {
  const { t, i18n } = useTranslation();
  const isArabic = i18n.language === 'ar';

  return (
    <Switch
      checked={isArabic}
      onChange={() => i18n.changeLanguage(isArabic ? 'en' : 'ar')}
      leftLabel="EN"
      rightLabel="عربي"
      tone={tone}
      ariaLabel={t('common.toggleLanguage')}
    />
  );
}
