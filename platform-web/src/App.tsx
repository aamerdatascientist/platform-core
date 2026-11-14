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
import { FormAssignments } from './routes/FormAssignments';
import { StakeholderReports } from './routes/StakeholderReports';
import { ProjectProgress } from './routes/ProjectProgress';
import { ProjectChooser } from './routes/ProjectChooser';
import { MessagingSchedule } from './routes/MessagingSchedule';
import { Today } from './routes/Today';
import { Settings } from './routes/Settings';
import { SignIn } from './routes/SignIn';
import { UserManagement } from './routes/UserManagement';
import { Account } from './routes/Account';
import { FormsList } from './routes/FormsList';
import { MyEntries } from './routes/MyEntries';
import { EntryDetail } from './routes/EntryDetail';
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
        <Route index element={<Today />} />
        <Route path="assignments" element={<Navigate to="/" replace />} />
        <Route path="forms" element={<FormsList />} />
        <Route path="forms/:formId" element={<FormView token={accessToken} />} />
        <Route path="entries" element={<MyEntries token={accessToken} />} />
        <Route path="entries/:formId/:recordId" element={<EntryDetail token={accessToken} />} />
        <Route path="settings" element={<Settings token={accessToken} />} />
        <Route path="account" element={<Account token={accessToken} />} />
        <Route element={<RequireAdmin token={accessToken} />}>
          <Route path="builder" element={<BuilderHome token={accessToken} />} />
          <Route path="builder/:formId" element={<FormBuilder token={accessToken} />} />
          <Route path="admin/users" element={<UserManagement token={accessToken} />} />
          <Route path="admin/assignments" element={<FormAssignments token={accessToken} />} />
          {/* Merged into /admin/assignments (screen 23, phase 8). */}
          <Route path="admin/assignments/status" element={<Navigate to="/admin/assignments" replace />} />
          <Route path="admin/reports" element={<StakeholderReports token={accessToken} />} />
          <Route path="admin/projects" element={<ProjectChooser token={accessToken} />} />
          <Route path="admin/projects/:projectId/progress" element={<ProjectProgress token={accessToken} />} />
          <Route path="admin/messages" element={<MessagingSchedule token={accessToken} />} />
        </Route>
        <Route path="*" element={<StatusPage kind="notFound" />} />
      </Route>
    </Routes>
  );
}
