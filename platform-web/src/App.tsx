import { useEffect, useState } from 'react';
import { Route, Routes, useLocation } from 'react-router-dom';
import { getTokens, subscribe } from './auth/tokenStore';
import { isDashboardPath } from './dashboardLink';
import { BuilderHome } from './routes/BuilderHome';
import { ExecutiveOverview } from './routes/ExecutiveOverview';
import { FormBuilder } from './routes/FormBuilder';
import { FormView } from './routes/FormView';
import { Layout } from './routes/Layout';
import { SignIn } from './routes/SignIn';
import { UserManagement } from './routes/UserManagement';
import { Welcome } from './routes/Welcome';
import { RequireAdmin } from './components/RequireAdmin';

/**
 * Real routing now (React Router) - refreshing or bookmarking /forms/{id} keeps your
 * place, unlike the earlier single-state version. Token refresh (client.ts) means you
 * shouldn't see 401 interruptions from normal 30-minute access-token expiry anymore.
 */
export default function App() {
  const [accessToken, setAccessToken] = useState<string | null>(() => getTokens()?.accessToken ?? null);

  useEffect(() => subscribe((tokens) => setAccessToken(tokens?.accessToken ?? null)), []);

  const location = useLocation();

  // The Executive Overview is a standalone page: no sidebar, and - when its link carries a
  // share key - no sign-in either. It is checked before the sign-in gate for that reason.
  if (isDashboardPath(location.pathname)) return <ExecutiveOverview token={accessToken} />;

  if (!accessToken) return <SignIn />;

  return (
    <Routes>
      <Route element={<Layout token={accessToken} />}>
        <Route index element={<Welcome />} />
        <Route path="forms/:formId" element={<FormView token={accessToken} />} />
        <Route element={<RequireAdmin token={accessToken} />}>
          <Route path="builder" element={<BuilderHome token={accessToken} />} />
          <Route path="builder/:formId" element={<FormBuilder token={accessToken} />} />
          <Route path="admin/users" element={<UserManagement token={accessToken} />} />
        </Route>
      </Route>
    </Routes>
  );
}
