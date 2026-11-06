import { useTranslation } from 'react-i18next';
import { NavLink, useLocation } from 'react-router-dom';
import { Icon } from '../icons';
import type { IconName } from '../icons';

/**
 * Phone bottom tab bar (HANDOFF §5.1, screens 02, 08, 09, 10): 80px + the safe area, four
 * equal tabs - Today, Forms, My entries, Settings. Active: --accent label weight 700 with the
 * icon in a 56x30 --accent-soft pill; inactive --ink-soft weight 500.
 */
export function TabBar() {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const tabs: { to: string; label: string; icon: IconName; active: boolean }[] = [
    { to: '/', label: t('nav.today'), icon: 'home', active: pathname === '/' },
    { to: '/forms', label: t('nav.forms'), icon: 'clipboard', active: pathname.startsWith('/forms') },
    { to: '/entries', label: t('nav.myEntries'), icon: 'list', active: pathname.startsWith('/entries') },
    {
      to: '/settings',
      label: t('nav.settings'),
      icon: 'sliders',
      active: /^\/(settings|account|builder|admin)(\/|$)/.test(pathname),
    },
  ];
  return (
    <nav
      aria-label={t('nav.main')}
      className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-4 border-t border-border bg-panel"
      style={{ height: 'calc(var(--tabbar-h) + env(safe-area-inset-bottom))', paddingBottom: 'calc(10px + env(safe-area-inset-bottom))' }}
    >
      {tabs.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          aria-current={tab.active ? 'page' : undefined}
          className={`flex flex-col items-center justify-center gap-1 text-[13px] ${tab.active ? 'font-bold text-accent' : 'font-medium text-ink-soft'}`}
        >
          <span className={`flex h-[30px] items-center justify-center ${tab.active ? 'w-14 rounded-ui-pill bg-accent-soft' : ''}`}>
            <Icon name={tab.icon} size={22} />
          </span>
          {tab.label}
        </NavLink>
      ))}
    </nav>
  );
}
