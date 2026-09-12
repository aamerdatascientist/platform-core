import { useEffect, useState } from 'react';
import { Navigate, Outlet } from 'react-router-dom';
import { api } from '../api/client';

interface RequireAdminProps {
  token: string;
}

/**
 * Route-level gate for /builder and /admin/users - previously only the sidebar hid these
 * links from non-admins (Layout's own isAdmin check); the routes themselves were reachable
 * by anyone who typed the URL directly. The backend already rejects the actual write
 * operations these pages trigger, but the pages themselves would still render and call
 * list/read endpoints in the meantime - this closes that gap at the client too.
 */
export function RequireAdmin({ token }: RequireAdminProps) {
  const [isAdmin, setIsAdmin] = useState<boolean | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.auth
      .me(token)
      .then((me) => {
        if (!cancelled) setIsAdmin(me.roles.includes('Administrator'));
      })
      .catch(() => {
        if (!cancelled) setIsAdmin(false);
      });
    return () => {
      cancelled = true;
    };
  }, [token]);

  if (isAdmin === null) return null;
  if (!isAdmin) return <Navigate to="/" replace />;
  return <Outlet />;
}
