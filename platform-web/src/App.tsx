import { useEffect, useState } from 'react';
import { api, ApiError } from './api/client';
import { FormPicker } from './components/FormPicker';
import { FormRenderer } from './components/FormRenderer';
import { SubmissionsTable } from './components/SubmissionsTable';
import type { DynamicRow, FormDefinitionDto } from './types';

/**
 * Still not the real app shell - no routing (refreshing loses your place), and the token
 * still has no refresh flow (expires after 30 min, sign out/in again when that happens).
 * But form navigation is now real, driven by GET /api/forms instead of pasting IDs by hand.
 */
export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('token'));
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loginError, setLoginError] = useState<string | null>(null);

  const [formDefinition, setFormDefinition] = useState<FormDefinitionDto | null>(null);
  const [submissions, setSubmissions] = useState<DynamicRow[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    if (token) localStorage.setItem('token', token);
    else localStorage.removeItem('token');
  }, [token]);

  async function handleLogin(e: React.FormEvent) {
    e.preventDefault();
    setLoginError(null);
    try {
      const tokens = await api.auth.login(email, password);
      setToken(tokens.accessToken);
    } catch (err) {
      setLoginError(err instanceof ApiError ? err.message : 'Login failed.');
    }
  }

  async function selectForm(formId: string) {
    if (!token) return;
    setLoadError(null);
    try {
      const def = await api.forms.get(token, formId);
      setFormDefinition(def);
      const page = await api.submissions.list(token, formId);
      setSubmissions(page.items);
    } catch (err) {
      setFormDefinition(null);
      setLoadError(err instanceof ApiError ? err.message : 'Could not load that form.');
    }
  }

  async function refreshSubmissions() {
    if (!token || !formDefinition) return;
    const page = await api.submissions.list(token, formDefinition.id);
    setSubmissions(page.items);
  }

  if (!token) {
    return (
      <div className="mx-auto mt-24 max-w-sm px-4">
        <h1 className="mb-6 text-xl font-medium text-gray-900">Sign in</h1>
        <form onSubmit={handleLogin} className="space-y-4">
          <input
            type="email"
            placeholder="Email"
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <input
            type="password"
            placeholder="Password"
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {loginError && <p className="text-sm text-red-600">{loginError}</p>}
          <button type="submit" className="w-full rounded-md bg-gray-900 px-4 py-2 text-sm font-medium text-white">
            Sign in
          </button>
        </form>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen">
      <aside className="w-64 shrink-0 border-r border-gray-200 px-4 py-6">
        <div className="mb-6 flex items-center justify-between">
          <h1 className="text-lg font-medium text-gray-900">Platform</h1>
          <button className="text-xs text-gray-500" onClick={() => setToken(null)}>
            Sign out
          </button>
        </div>
        <FormPicker token={token} onSelect={selectForm} selectedFormId={formDefinition?.id} />
      </aside>

      <main className="flex-1 px-8 py-10">
        {loadError && <p className="mb-4 text-sm text-red-600">{loadError}</p>}

        {!formDefinition && !loadError && (
          <p className="text-sm text-gray-400">Pick a form from the left to get started.</p>
        )}

        {formDefinition && (
          <div className="max-w-2xl space-y-8">
            <h2 className="text-lg font-medium text-gray-900">{formDefinition.name}</h2>
            <FormRenderer token={token} formDefinition={formDefinition} onSubmitted={refreshSubmissions} />
            <div>
              <h3 className="mb-3 text-sm font-medium text-gray-500">Records</h3>
              <SubmissionsTable fields={formDefinition.publishedVersion?.fields ?? []} rows={submissions} />
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
