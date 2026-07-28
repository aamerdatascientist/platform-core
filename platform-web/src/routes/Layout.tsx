import { NavLink, Outlet } from 'react-router-dom';
import { api } from '../api/client';
import { getTokens, setTokens } from '../auth/tokenStore';
import { FormPicker } from '../components/FormPicker';

interface LayoutProps {
  token: string;
}

export function Layout({ token }: LayoutProps) {
  function handleSignOut() {
    const stored = getTokens();
    // Best-effort - don't block signing out locally on a network round-trip.
    if (stored) api.auth.logout(stored.refreshToken).catch(() => {});
    setTokens(null);
  }

  return (
    <div className="flex min-h-screen bg-paper">
      <aside className="w-64 shrink-0 border-r border-line px-4 py-6">
        <div className="mb-8 flex items-center justify-between">
          <h1 className="font-display text-lg font-semibold text-ink">Platform</h1>
          <button
            onClick={handleSignOut}
            className="font-mono text-[11px] uppercase tracking-wide text-ink-muted hover:text-ink"
          >
            Sign out
          </button>
        </div>
        <FormPicker token={token} />

        <div className="mt-8 border-t border-line pt-4">
          <NavLink
            to="/builder"
            className={({ isActive }) =>
              `font-mono text-[11px] uppercase tracking-wide ${isActive ? 'text-ink' : 'text-ink-muted hover:text-ink'}`
            }
          >
            + Build forms
          </NavLink>
        </div>
      </aside>

      <main className="flex-1 px-8 py-10">
        <Outlet />
      </main>
    </div>
  );
}
