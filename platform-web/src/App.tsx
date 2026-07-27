import { useEffect, useState } from 'react';
import { api, ApiError } from './api/client';
import { FormRenderer } from './components/FormRenderer';
import { SubmissionsTable } from './components/SubmissionsTable';
import type { DynamicRow, FormDefinitionDto } from './types';

/**
 * Deliberately minimal: this proves the FormRenderer works end to end against the real
 * API, it isn't the real app shell. Two gaps worth knowing about before extending this:
 *
 * 1. There's no GET /api/forms (list) endpoint yet, so this asks for a form ID by hand
 *    instead of showing a picker. Small backend addition needed before a real nav exists.
 * 2. The token is kept in memory + localStorage with no refresh-token flow wired up -
 *    it'll silently start failing when the access token expires (30 min by default).
 */
export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('token'));
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loginError, setLoginError] = useState<string | null>(null);

  const [formId, setFormId] = useState('');
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

  async function loadForm() {
    if (!token || !formId) return;
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
    <div className="mx-auto max-w-2xl px-4 py-10">
      <div className="mb-8 flex items-center justify-between">
        <h1 className="text-xl font-medium text-gray-900">Platform</h1>
        <button className="text-sm text-gray-500" onClick={() => setToken(null)}>
          Sign out
        </button>
      </div>

      <div className="mb-8 flex gap-2">
        <input
          type="text"
          placeholder="Form definition ID"
          className="flex-1 rounded-md border border-gray-300 px-3 py-2 text-sm"
          value={formId}
          onChange={(e) => setFormId(e.target.value)}
        />
        <button onClick={loadForm} className="rounded-md border border-gray-300 px-4 py-2 text-sm">
          Load form
        </button>
      </div>
      {loadError && <p className="mb-4 text-sm text-red-600">{loadError}</p>}

      {formDefinition && (
        <div className="space-y-8">
          <div>
            <h2 className="mb-4 text-lg font-medium text-gray-900">{formDefinition.name}</h2>
            <FormRenderer token={token} formDefinition={formDefinition} onSubmitted={refreshSubmissions} />
          </div>
          <div>
            <h3 className="mb-3 text-sm font-medium text-gray-500">Records</h3>
            <SubmissionsTable fields={formDefinition.publishedVersion?.fields ?? []} rows={submissions} />
          </div>
        </div>
      )}
    </div>
  );
}
