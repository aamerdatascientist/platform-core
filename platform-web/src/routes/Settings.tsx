import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { getTokens, setTokens } from '../auth/tokenStore';
import { ChangePasswordForm } from '../components/ChangePasswordForm';
import { LanguageToggle } from '../components/LanguageToggle';
import { ModeToggle } from '../components/ModeToggle';
import { buildDashboardUrl } from '../dashboardLink';
import { getMode } from '../theme/mode';
import type { CurrentUserDto } from '../types';

interface SettingsProps {
  token: string;
}

/**
 * Everything that isn't filling in a form lives here (2026-10-21), so the sidebar holds only
 * the forms people enter data into, plus this page: your account (password, sign out), the
 * display language and light/dark mode, and - for administrators only - the dashboard, the
 * form builder and users & roles.
 */
export function Settings({ token }: SettingsProps) {
  const { t, i18n } = useTranslation();
  const [me, setMe] = useState<CurrentUserDto | null>(null);
  const [dashboardKey, setDashboardKey] = useState<string | null>(null);
  const isAdmin = me?.roles.includes('Administrator') ?? false;

  useEffect(() => {
    api.auth.me(token).then(setMe).catch(() => setMe(null));
  }, [token]);

  // Fetched up front, not on click: the dashboard opens in a new tab, and a browser only
  // allows that directly inside the click itself - waiting on a request first would get the
  // new tab blocked as a pop-up. Null (public viewing switched off on the server) is fine;
  // the tab then opens on the signed-in session instead.
  useEffect(() => {
    if (!isAdmin) return;
    api.analytics
      .executiveOverviewShareKey(token)
      .then((result) => setDashboardKey(result.key))
      .catch(() => setDashboardKey(null));
  }, [token, isAdmin]);

  /**
   * Opens the Executive Overview in its own tab, as a full-window page. With a share key the
   * tab needs nothing from this one. Without it, the tab relies on being opened by this
   * window, which is why this is window.open and not a plain link.
   */
  function openDashboard() {
    window.open(buildDashboardUrl({ shareKey: dashboardKey, language: i18n.language, mode: getMode() }), '_blank');
  }

  function handleSignOut() {
    const stored = getTokens();
    // Best-effort - don't block signing out locally on a network round-trip.
    if (stored) api.auth.logout(stored.refreshToken).catch(() => {});
    setTokens(null);
  }

  const sectionClass = 'rounded border border-border bg-panel p-4 shadow-recessed sm:p-5';
  const headingClass = 'mb-4 text-label font-medium uppercase tracking-wider text-ink-soft';
  const rowButtonClass =
    'flex w-full items-center justify-between gap-3 rounded border border-border bg-bg px-4 py-3 text-start hover:border-accent';

  return (
    <div className="max-w-2xl space-y-6">
      <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{t('settings.title')}</h2>

      <section className={sectionClass} aria-labelledby="settings-account">
        <h3 id="settings-account" className={headingClass}>
          {t('settings.account')}
        </h3>
        {me && (
          <div className="mb-5">
            <p className="text-sm font-medium text-ink">{me.displayName}</p>
            <p dir="ltr" className="text-xs text-ink-soft rtl:text-end">
              {me.email}
            </p>
          </div>
        )}
        <h4 className="mb-2 text-sm font-medium text-ink">{t('account.title')}</h4>
        <ChangePasswordForm token={token} />
        <div className="mt-6 border-t border-border pt-4">
          <button
            type="button"
            onClick={handleSignOut}
            className="rounded border border-danger bg-panel px-4 py-2 text-sm text-danger hover:bg-bg"
          >
            {t('sidebar.signOut')}
          </button>
        </div>
      </section>

      <section className={sectionClass} aria-labelledby="settings-display">
        <h3 id="settings-display" className={headingClass}>
          {t('settings.display')}
        </h3>
        <div className="space-y-4">
          <div className="flex items-center justify-between gap-4">
            <span className="text-sm text-ink">{t('settings.language')}</span>
            <LanguageToggle tone="light" />
          </div>
          <div className="flex items-center justify-between gap-4">
            <span className="text-sm text-ink">{t('settings.appearance')}</span>
            <ModeToggle tone="light" />
          </div>
        </div>
      </section>

      {isAdmin && (
        <section className={sectionClass} aria-labelledby="settings-admin">
          <h3 id="settings-admin" className={headingClass}>
            {t('settings.administration')}
          </h3>
          <div className="space-y-2">
            <button type="button" onClick={openDashboard} className={rowButtonClass}>
              <span>
                <span className="block text-sm font-medium text-ink">{t('sidebar.executiveOverview')}</span>
                <span className="block text-xs text-ink-soft">{t('settings.dashboardHint')}</span>
              </span>
              <span aria-hidden="true" className="text-ink-soft">
                ↗
              </span>
            </button>
            <Link to="/builder" className={rowButtonClass}>
              <span>
                <span className="block text-sm font-medium text-ink">{t('settings.formBuilder')}</span>
                <span className="block text-xs text-ink-soft">{t('settings.formBuilderHint')}</span>
              </span>
              <span aria-hidden="true" className="text-ink-soft rtl:rotate-180">
                ›
              </span>
            </Link>
            <Link to="/admin/users" className={rowButtonClass}>
              <span>
                <span className="block text-sm font-medium text-ink">{t('userManagement.title')}</span>
                <span className="block text-xs text-ink-soft">{t('settings.usersHint')}</span>
              </span>
              <span aria-hidden="true" className="text-ink-soft rtl:rotate-180">
                ›
              </span>
            </Link>
          </div>
        </section>
      )}
    </div>
  );
}
