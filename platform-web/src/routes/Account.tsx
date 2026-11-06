import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { ChangePasswordForm } from '../components/ChangePasswordForm';
import { Icon } from '../components/icons';
import { Card } from '../components/ui';

/**
 * Change password (restored at /account, owner 2026-11-06; reached from Settings > Account).
 * The form and its API call are unchanged; it is restyled with the new components in phase 4.
 */
export function Account({ token }: { token: string }) {
  const { t } = useTranslation();
  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-5">
      <Link to="/settings" className="flex min-h-[44px] items-center gap-1.5 self-start text-[15px] text-accent">
        <Icon name="chevronBack" size={18} />
        {t('nav.settings')}
      </Link>
      <h1 className="text-[26px] font-extrabold text-ink lg:text-[28px]">{t('account.title')}</h1>
      <Card className="p-5">
        <ChangePasswordForm token={token} />
      </Card>
    </div>
  );
}
