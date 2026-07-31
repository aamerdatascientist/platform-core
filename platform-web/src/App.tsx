import { useEffect, useState } from 'react';
import { Route, Routes } from 'react-router-dom';
import { getTokens, subscribe } from './auth/tokenStore';
import { BuilderHome } from './routes/BuilderHome';
import { FormBuilder } from './routes/FormBuilder';
import { FormView } from './routes/FormView';
import { Layout } from './routes/Layout';
import { SignIn } from './routes/SignIn';
import { UserManagement } from './routes/UserManagement';
import { Welcome } from './routes/Welcome';

/**
 * Real routing now (React Router) - refreshing or bookmarking /forms/{id} keeps your
 * place, unlike the earlier single-state version. Token refresh (client.ts) means you
 * shouldn't see 401 interruptions from normal 30-minute access-token expiry anymore.
 */
export default function App() {
  const [accessToken, setAccessToken] = useState<string | null>(() => getTokens()?.accessToken ?? null);

  useEffect(() => subscribe((tokens) => setAccessToken(tokens?.accessToken ?? null)), []);

  if (!accessToken) return <SignIn />;

  return (
    <Routes>
      <Route element={<Layout token={accessToken} />}>
        <Route index element={<Welcome />} />
        <Route path="forms/:formId" element={<FormView token={accessToken} />} />
        <Route path="builder" element={<BuilderHome token={accessToken} />} />
        <Route path="builder/:formId" element={<FormBuilder token={accessToken} />} />
        <Route path="admin/users" element={<UserManagement token={accessToken} />} />
      </Route>
    </Routes>
  );
}
