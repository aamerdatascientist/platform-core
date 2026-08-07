import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { api } from '../api/client';
import { getTokens, setTokens } from '../auth/tokenStore';
import { FormPicker } from '../components/FormPicker';
import { Logo } from '../components/Logo';

interface LayoutProps {
  token: string;
}

export function Layout({ token }: LayoutProps) {
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
    `block text-[11px] uppercase tracking-wide ${isActive ? 'text-white' : 'text-sidebar-muted hover:text-white'}`;

  return (
    // h-screen + overflow-hidden here, not min-h-screen, is what actually makes the
    // sidebar "stick" - the page itself never scrolls; each column below scrolls
    // independently within its own fixed-height box instead.
    <div className="flex h-screen overflow-hidden bg-paper">
      {/* Mobile-only top bar - just branding and a toggle, shown when the drawer is closed */}
      <div className="fixed inset-x-0 top-0 z-20 flex items-center justify-between border-b border-sidebar-border bg-sidebar px-3 py-3 lg:hidden">
        <div className="flex items-center gap-2">
          <Logo size="sm" />
          <span className="font-display text-sm font-semibold text-sidebar-text">ASAS</span>
        </div>
        <button
          onClick={() => setMobileMenuOpen(true)}
          className="px-2 text-xl leading-none text-sidebar-text"
          aria-label="Open menu"
        >
          ☰
        </button>
      </div>

      {mobileMenuOpen && (
        <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setMobileMenuOpen(false)} />
      )}

      <aside
        className={`fixed inset-y-0 left-0 z-40 flex w-64 shrink-0 flex-col overflow-y-auto bg-sidebar px-3 py-5 transition-transform duration-200 lg:static lg:z-auto lg:w-56 lg:translate-x-0 ${
          mobileMenuOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className="mb-5 flex items-center justify-between px-1">
          <div className="flex items-center gap-2">
            <Logo size="sm" />
            <span className="font-display text-sm font-semibold tracking-wide text-sidebar-text">ASAS</span>
          </div>
          <button
            onClick={handleSignOut}
            className="text-[11px] uppercase tracking-wide text-sidebar-muted hover:text-white"
          >
            Sign out
          </button>
        </div>
        <FormPicker token={token} />

        <div className="mt-6 space-y-2 border-t border-sidebar-border pt-3">
          {isAdmin && (
            <>
              <NavLink to="/builder" className={navLinkClass}>
                + Build forms
              </NavLink>
              <NavLink to="/admin/users" className={navLinkClass}>
                Users
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
