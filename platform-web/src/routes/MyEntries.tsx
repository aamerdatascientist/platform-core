import { useTranslation } from 'react-i18next';
import { EmptyState } from '../components/ui';

/**
 * My entries (screen 08). The list needs the new own-submissions API (HANDOFF §9.3), which
 * comes in phase 7; until then the tab exists and says what will be here.
 */
export function MyEntries() {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-5">
      <h1 className="text-[26px] font-extrabold text-ink lg:text-[28px]">{t('entries.title')}</h1>
      <EmptyState title={t('entries.title')} body={t('entries.comingSoon')} />
    </div>
  );
}
