import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { getTokens, setTokens } from '../auth/tokenStore';
import { Icon } from '../components/icons';
import { initial, useShell } from '../components/shell/ShellData';
import { TelegramConnect } from '../components/TelegramConnect';
import { Card, Pill, Segmented } from '../components/ui';
import { getModePreference, onModeChanged, setModePreference } from '../theme/mode';
import { NAV_ROUTES, PHONE_ADMIN_ORDER, type NavRouteId } from '../nav/navMap';

const ADMIN_HINTS: Record<NavRouteId, string> = {
  users: 'settings.usersHint',
  assignments: 'settings.assignmentsHint',
  messages: 'settings.messagesHint',
  reports: 'settings.reportsHint',
  overview: 'settings.dashboardHint',
  stageProgress: 'settings.stageProgressHint',
  builder: 'settings.formBuilderHint',
};
import type { ModePreference } from '../theme/mode';

interface SettingsProps {
  token: string;
}

/**
 * Settings (screen 09): the person's profile; Display (language, and appearance with "Match
 * phone" following the phone's own setting); Reminders (Telegram); Account (change password at
 * /account, sign out); and, for administrators, the admin pages - on a phone this is how they
 * reach them. On a desktop the same sections in one 720px column.
 */
export function Settings({ token }: SettingsProps) {
  const { t, i18n } = useTranslation();
  const { me, isAdmin, openDashboard, roleLabel } = useShell();
  const [preference, setPreference] = useState<ModePreference>(getModePreference());
  useEffect(() => onModeChanged(() => setPreference(getModePreference())), []);

  function handleSignOut() {
    const stored = getTokens();
    // Best-effort - don't block signing out locally on a network round-trip.
    if (stored) api.auth.logout(stored.refreshToken).catch(() => {});
    setTokens(null);
  }

  const row = 'flex min-h-[54px] w-full items-center gap-3 px-4 text-start text-[16px] font-medium hover:bg-field-muted';

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-[18px]">
      <h1 className="text-[26px] font-extrabold text-ink lg:text-[28px]">{t('settings.title')}</h1>

      {me && (
        <Card kind="phone-secondary" className="flex items-center gap-3 px-4 py-[14px]">
          <span aria-hidden="true" className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-accent-soft text-[19px] font-extrabold text-accent">
            {initial(me.displayName)}
          </span>
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <span className="truncate text-[17px] font-bold text-ink">{me.displayName}</span>
            <span dir="ltr" className="truncate text-[13px] text-ink-soft rtl:text-right">
              {me.email}
            </span>
          </div>
          {me.roles.slice(0, 1).map((r) => (
            <Pill key={r} tone="info" className="!px-[10px] !py-1">
              {roleLabel(r)}
            </Pill>
          ))}
        </Card>
      )}

      <Section title={t('settings.display')}>
        <Card kind="phone-secondary" className="flex flex-col gap-[14px] px-4 py-[14px]">
          <div className="flex flex-col gap-2">
            <span className="text-[16px] font-medium text-ink">{t('settings.language')}</span>
            <Segmented
              label={t('settings.language')}
              value={i18n.language.startsWith('ar') ? 'ar' : 'en'}
              onChange={(v) => void i18n.changeLanguage(v)}
              options={[
                { value: 'ar', label: t('settingsV2.languageArabic') },
                { value: 'en', label: t('settingsV2.languageEnglish') },
              ]}
            />
          </div>
          <div className="flex flex-col gap-2">
            <span className="text-[16px] font-medium text-ink">{t('settingsV2.appearance')}</span>
            <Segmented
              label={t('settingsV2.appearance')}
              value={preference}
              onChange={(v) => {
                setPreference(v);
                setModePreference(v);
              }}
              options={[
                { value: 'light', label: t('settingsV2.appearanceLight') },
                { value: 'dark', label: t('settingsV2.appearanceDark') },
                { value: 'system', label: t('settingsV2.appearanceSystem') },
              ]}
            />
          </div>
        </Card>
      </Section>

      <TelegramConnect token={token} />

      <Section title={t('settingsV2.accountSection')}>
        <Card kind="phone-secondary" className="divide-y divide-divider overflow-hidden">
          <Link to="/account" className={`${row} text-ink`}>
            <Icon name="lock" size={20} className="text-ink-soft" />
            <span className="flex-1">{t('sidebar.changePassword')}</span>
            <Icon name="chevronForward" size={18} className="text-ink-soft" />
          </Link>
          <button type="button" onClick={handleSignOut} className={`${row} text-danger`}>
            <Icon name="logout" size={20} />
            <span className="flex-1">{t('sidebar.signOut')}</span>
          </button>
        </Card>
      </Section>

      {isAdmin && (
        <Section title={t('settings.administration')}>
          <Card kind="phone-secondary" className="divide-y divide-divider overflow-hidden">
            {/* Admin flows F5-9: the order administrators work in. */}
            {PHONE_ADMIN_ORDER.map((id) => {
              const r = NAV_ROUTES[id];
              return r.to === null ? (
                <AdminRow key={id} onClick={openDashboard} title={t(r.labelKey)} hint={t(ADMIN_HINTS[id])} external />
              ) : (
                <AdminRow key={id} to={r.to} title={t(r.labelKey)} hint={t(ADMIN_HINTS[id])} />
              );
            })}
          </Card>
        </Section>
      )}
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-2">
      <h2 className="px-1 text-[14px] font-bold text-ink-soft">{title}</h2>
      {children}
    </section>
  );
}

function AdminRow({ to, onClick, title, hint, external }: { to?: string; onClick?: () => void; title: string; hint: string; external?: boolean }) {
  const body = (
    <>
      <span className="flex min-w-0 flex-1 flex-col gap-0.5 py-2">
        <span className="text-[16px] font-medium text-ink">{title}</span>
        <span className="text-[13px] text-ink-soft">{hint}</span>
      </span>
      <Icon name={external ? 'external' : 'chevronForward'} size={18} className="text-ink-soft" />
    </>
  );
  const cls = 'flex min-h-[56px] w-full items-center gap-3 px-4 text-start hover:bg-field-muted';
  return to ? (
    <Link to={to} className={cls}>
      {body}
    </Link>
  ) : (
    <button type="button" onClick={onClick} className={cls}>
      {body}
    </button>
  );
}
