import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { api } from '../api/client';
import { getTokens, setTokens } from '../auth/tokenStore';
import { FormPicker } from '../components/FormPicker';
import { LanguageToggle } from '../components/LanguageToggle';
import { Logo } from '../components/Logo';
import { ModeToggle } from '../components/ModeToggle';

interface LayoutProps {
  token: string;
}

export function Layout({ token }: LayoutProps) {
  const { t } = useTranslation();
  const [isAdmin, setIsAdmin] = useState(false);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const location = useLocation();

  useEffect(() => {
    api.auth
      .me(token)
      .then((me) => setIsAdmin(me.roles.includes('Administrator')))
      .catch(() => setIsAdmin(false));
  }, [token]);

  // Close the drawer automatically on navigation - otherwise picking a form from it on
  // mobile would leave the drawer covering the screen instead of showing the new page.
  useEffect(() => {
    setMobileMenuOpen(false);
  }, [location.pathname]);

  function handleSignOut() {
    const stored = getTokens();
    // Best-effort - don't block signing out locally on a network round-trip.
    if (stored) api.auth.logout(stored.refreshToken).catch(() => {});
    setTokens(null);
  }

  const navLinkClass = ({ isActive }: { isActive: boolean }) =>
    `block text-[11px] uppercase tracking-wide ${isActive ? 'text-sidebar-ink-strong' : 'text-sidebar-ink hover:text-sidebar-ink-strong'}`;

  return (
    // h-screen + overflow-hidden here, not min-h-screen, is what actually makes the
    // sidebar "stick" - the page itself never scrolls; each column below scrolls
    // independently within its own fixed-height box instead.
    <div className="flex h-screen overflow-hidden bg-bg">
      {/* Mobile-only top bar - just branding and a toggle, shown when the drawer is closed.
          relative + the rivet-strip as its last child replaces the old plain border-b -
          the rivet-strip IS the divider here, not an addition on top of one. */}
      <div className="fixed inset-x-0 top-0 z-20 flex items-center justify-between bg-sidebar px-3 py-3 lg:hidden">
        <div className="flex items-center gap-2">
          <Logo size="sm" />
          <span className="font-display text-sm font-semibold uppercase tracking-[0.07em] text-sidebar-ink-strong">ASAS</span>
        </div>
        <button
          onClick={() => setMobileMenuOpen(true)}
          className="px-2 text-xl leading-none text-sidebar-ink-strong"
          aria-label={t('sidebar.openMenu')}
        >
          ☰
        </button>
        <div className="rivet-strip absolute inset-x-0 bottom-0" />
      </div>

      {mobileMenuOpen && (
        <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setMobileMenuOpen(false)} />
      )}

      <aside
        className={`fixed inset-y-0 start-0 z-40 flex w-64 shrink-0 flex-col overflow-y-auto bg-sidebar px-3 py-5 transition-transform duration-200 lg:static lg:z-auto lg:w-56 lg:translate-x-0 ${
          mobileMenuOpen ? 'translate-x-0' : '-translate-x-full max-lg:rtl:translate-x-full'
        }`}
      >
        <div className="mb-3 flex items-center justify-between px-1">
          <div className="flex items-center gap-2">
            <Logo size="sm" />
            <span className="font-display text-sm font-semibold uppercase tracking-[0.07em] text-sidebar-ink-strong">ASAS</span>
          </div>
          <button
            onClick={handleSignOut}
            className="text-[11px] uppercase tracking-wide text-sidebar-ink hover:text-sidebar-ink-strong"
          >
            {t('sidebar.signOut')}
          </button>
        </div>
        <div className="rivet-strip mb-4" />
        {/* dir="ltr" here, not just on each Switch individually - otherwise this row
            itself reorders under an inherited RTL context even though each toggle's
            own internal layout stays correct on its own. flex-wrap is a safety net,
            not the primary fix - a fixed physical position matters more than a tight
            one-line fit if the sidebar is ever narrower than both switches combined. */}
        <div dir="ltr" className="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 px-1">
          <LanguageToggle />
          <ModeToggle />
        </div>
        <FormPicker token={token} />

        <div className="mt-6 space-y-2 border-t border-border rounded pt-3">
          <NavLink to="/dashboards/executive-overview" className={navLinkClass}>
            {t('sidebar.executiveOverview')}
          </NavLink>
          {isAdmin && (
            <>
              <NavLink to="/builder" className={navLinkClass}>
                {t('sidebar.buildForms')}
              </NavLink>
              <NavLink to="/admin/users" className={navLinkClass}>
                {t('sidebar.users')}
              </NavLink>
            </>
          )}
        </div>
      </aside>

      <main className="flex-1 overflow-y-auto px-4 pb-6 pt-20 sm:px-6 sm:pb-8 lg:px-6 lg:py-8 lg:pt-8">
        <Outlet />
      </main>
    </div>
  );
}
