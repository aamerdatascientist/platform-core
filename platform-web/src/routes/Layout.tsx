import { useEffect, useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { api } from '../api/client';
import { getTokens, setTokens } from '../auth/tokenStore';
import { FormPicker } from '../components/FormPicker';
import { Logo } from '../components/Logo';

interface LayoutProps {
  token: string;
}

export function Layout({ token }: LayoutProps) {
  const [isAdmin, setIsAdmin] = useState(false);

  useEffect(() => {
    api.auth
      .me(token)
      .then((me) => setIsAdmin(me.roles.includes('Administrator')))
      .catch(() => setIsAdmin(false));
  }, [token]);

  function handleSignOut() {
    const stored = getTokens();
    // Best-effort - don't block signing out locally on a network round-trip.
    if (stored) api.auth.logout(stored.refreshToken).catch(() => {});
    setTokens(null);
  }

  const navLinkClass = ({ isActive }: { isActive: boolean }) =>
    `block text-[11px] uppercase tracking-wide ${isActive ? 'text-white' : 'text-sidebar-muted hover:text-white'}`;

  return (
    <div className="flex min-h-screen bg-paper">
      <aside className="w-56 shrink-0 bg-sidebar px-3 py-5">
        <div className="mb-5 flex items-center justify-between px-1">
          <div className="flex items-center gap-2">
            <Logo size="sm" />
            <span className="font-display text-sm font-semibold tracking-wide text-sidebar-text">NEXUS</span>
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

      <main className="flex-1 px-6 py-8">
        <Outlet />
      </main>
    </div>
  );
}
