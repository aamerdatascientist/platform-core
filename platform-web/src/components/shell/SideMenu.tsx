import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { NavLink, useLocation } from 'react-router-dom';
import { Icon, LogoMark } from '../icons';
import { initial, useFormsByModule, useShell } from './ShellData';
import { useLocalized } from '../../forms/localized';

/**
 * Desktop side menu (HANDOFF §5.2, screens 20-24): 264px on the inline-start side, the logo,
 * a search/jump button (Ctrl K), "Today · My tasks" with the count still due today, one group
 * per module listing its forms, then Administration (admins only), and the person at the
 * bottom with a Settings button. Scrolls with the page; not sticky.
 */
export function SideMenu({ onSearch }: { onSearch: () => void }) {
  const { t } = useTranslation();
  const { me, isAdmin, dueTodayCount, openDashboard, roleLabel } = useShell();
  const groups = useFormsByModule();
  const { formName } = useLocalized();
  const location = useLocation();
  const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);

  return (
    <nav
      aria-label={t('nav.main')}
      className="flex w-[264px] shrink-0 flex-col gap-[18px] border-e border-border bg-sidebar px-[14px] py-[18px]"
    >
      <div className="flex items-center gap-[10px] px-[6px]">
        <LogoMark size={34} />
        <span className="text-[20px] font-extrabold text-ink">{t('nav.appName')}</span>
      </div>

      <button
        type="button"
        onClick={onSearch}
        className="flex h-[44px] items-center gap-2 rounded-ui-lg border border-border bg-field-muted px-3 text-[14px] text-ink-soft hover:border-ink-soft"
      >
        <Icon name="search" size={17} />
        <span className="flex-1 text-start">{t('nav.searchOrJump')}</span>
        <span dir="ltr" className="rounded-ui-xs border border-border bg-panel px-1.5 py-px text-[12px]">
          {isMac ? '⌘ K' : 'Ctrl K'}
        </span>
      </button>

      <div className="flex flex-col gap-0.5">
        <Item to="/" end>
          <span className="flex-1">{t('nav.todayTasks')}</span>
          {dueTodayCount > 0 && (
            <span className="rounded-ui-pill bg-warning-soft px-2 py-0.5 text-[12px] font-bold text-warning">{dueTodayCount}</span>
          )}
        </Item>
      </div>

      {groups.map((group) => (
        <Group key={group.label} title={group.label}>
          {group.forms.map((form) => (
            <Item key={form.id} to={`/forms/${form.id}`} active={location.pathname.startsWith(`/forms/${form.id}`)}>
              <span className="flex-1"><bdi>{formName(form)}</bdi></span>
            </Item>
          ))}
        </Group>
      ))}

      {isAdmin && (
        <Group title={t('nav.administration')}>
          <button
            type="button"
            onClick={openDashboard}
            className="flex min-h-[44px] items-center gap-2 rounded-ui-md px-[10px] text-start text-[15px] font-medium text-ink hover:bg-field-muted"
            aria-label={`${t('sidebar.executiveOverview')} (${t('nav.opensNewTab')})`}
          >
            <span>{t('sidebar.executiveOverview')}</span>
            <Icon name="external" size={16} />
          </button>
          <Item to="/builder" active={location.pathname.startsWith('/builder')}>
            <span className="flex-1">{t('settings.formBuilder')}</span>
          </Item>
          <Item to="/admin/assignments">
            <span className="flex-1">{t('settings.assignments')}</span>
          </Item>
          <Item to="/admin/reports">
            <span className="flex-1">{t('reports.title')}</span>
          </Item>
          <Item to="/admin/users">
            <span className="flex-1">{t('userManagement.title')}</span>
          </Item>
        </Group>
      )}

      <div className="mt-auto flex items-center gap-[10px] border-t border-divider pt-[14px]">
        <span aria-hidden="true" className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-accent-soft font-extrabold text-accent">
          {initial(me?.displayName)}
        </span>
        <span className="flex min-w-0 flex-1 flex-col">
          <span className="truncate text-[15px] font-bold text-ink">{me?.displayName}</span>
          <span className="truncate text-[12px] text-ink-soft">{me?.roles.map(roleLabel).join(' · ')}</span>
        </span>
        <NavLink
          to="/settings"
          aria-label={t('nav.settings')}
          title={t('nav.settings')}
          className={({ isActive }) =>
            `flex h-11 w-11 items-center justify-center rounded-ui-md hover:bg-field-muted ${isActive ? 'bg-accent-soft text-accent-strong' : 'text-ink-soft'}`
          }
        >
          <Icon name="sliders" size={20} />
        </NavLink>
      </div>
    </nav>
  );
}

function Group({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="px-[10px] pb-1.5 text-[12px] font-bold text-ink-soft">{title}</span>
      {children}
    </div>
  );
}

function Item({ to, end, active, children }: { to: string; end?: boolean; active?: boolean; children: ReactNode }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        `flex min-h-[44px] items-center gap-2 rounded-ui-md px-[10px] text-[15px] ${
          active ?? isActive ? 'bg-accent-soft font-bold text-accent-strong' : 'font-medium text-ink hover:bg-field-muted'
        }`
      }
      aria-current={active ? 'page' : undefined}
    >
      {children}
    </NavLink>
  );
}
