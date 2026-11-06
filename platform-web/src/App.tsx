import { useEffect, useState } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { clearSessionEnded, getTokens, sessionEnded, subscribe } from './auth/tokenStore';
import { StatusPage } from './components/StatusPage';
import { isDashboardPath } from './dashboardLink';
import { BuilderHome } from './routes/BuilderHome';
import { ExecutiveOverview } from './routes/ExecutiveOverview';
import { FormBuilder } from './routes/FormBuilder';
import { FormView } from './routes/FormView';
import { Layout } from './routes/Layout';
import { AssignmentTracking } from './routes/AssignmentTracking';
import { FormAssignments } from './routes/FormAssignments';
import { StakeholderReports } from './routes/StakeholderReports';
import { MyAssignments } from './routes/MyAssignments';
import { Settings } from './routes/Settings';
import { SignIn } from './routes/SignIn';
import { UserManagement } from './routes/UserManagement';
import { Welcome } from './routes/Welcome';
import { RequireAdmin } from './components/RequireAdmin';
import { DevGallery } from './routes/DevGallery';

/**
 * Real routing now (React Router) - refreshing or bookmarking /forms/{id} keeps your
 * place, unlike the earlier single-state version. Token refresh (client.ts) means you
 * shouldn't see 401 interruptions from normal 30-minute access-token expiry anymore.
 */
export default function App() {
  const [accessToken, setAccessToken] = useState<string | null>(() => getTokens()?.accessToken ?? null);
  const [showSessionEnded, setShowSessionEnded] = useState(sessionEnded);

  useEffect(
    () =>
      subscribe((tokens) => {
        setAccessToken(tokens?.accessToken ?? null);
        setShowSessionEnded(sessionEnded());
      }),
    [],
  );

  const location = useLocation();

  // The Executive Overview is a standalone page: no sidebar, and - when its link carries a
  // share key - no sign-in either. It is checked before the sign-in gate for that reason.
  if (isDashboardPath(location.pathname)) return <ExecutiveOverview token={accessToken} />;

  // Development only: the redesign component gallery (never in a production build).
  if (import.meta.env.DEV && location.pathname === '/dev/ui') return <DevGallery />;

  if (!accessToken && showSessionEnded) {
    return (
      <StatusPage
        kind="sessionEnded"
        onSignIn={() => {
          clearSessionEnded();
          setShowSessionEnded(false);
        }}
      />
    );
  }
  if (!accessToken) return <SignIn />;

  return (
    <Routes>
      <Route element={<Layout token={accessToken} />}>
        <Route index element={<Welcome />} />
        <Route path="forms/:formId" element={<FormView token={accessToken} />} />
        <Route path="assignments" element={<MyAssignments token={accessToken} />} />
        <Route path="settings" element={<Settings token={accessToken} />} />
        <Route path="account" element={<Navigate to="/settings" replace />} />
        <Route element={<RequireAdmin token={accessToken} />}>
          <Route path="builder" element={<BuilderHome token={accessToken} />} />
          <Route path="builder/:formId" element={<FormBuilder token={accessToken} />} />
          <Route path="admin/users" element={<UserManagement token={accessToken} />} />
          <Route path="admin/assignments" element={<FormAssignments token={accessToken} />} />
          <Route path="admin/assignments/status" element={<AssignmentTracking token={accessToken} />} />
          <Route path="admin/reports" element={<StakeholderReports token={accessToken} />} />
        </Route>
        <Route path="*" element={<StatusPage kind="notFound" />} />
      </Route>
    </Routes>
  );
}
