import { useTranslation } from 'react-i18next';

export function Welcome() {
  const { t } = useTranslation();
  return (
    <div className="flex h-[60vh] flex-col items-center justify-center text-center">
      <p className="font-display text-lg text-ink">{t('welcome.title')}</p>
      <p className="mt-1 text-sm text-ink-muted">{t('welcome.subtitle')}</p>
    </div>
  );
}
