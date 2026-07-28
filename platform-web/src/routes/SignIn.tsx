import { useState } from 'react';
import { api, ApiError } from '../api/client';
import { setTokens } from '../auth/tokenStore';

export function SignIn() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const tokens = await api.auth.login(email, password);
      setTokens(tokens);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Check your email and password and try again.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-paper px-4">
      <div className="w-full max-w-sm">
        <h1 className="mb-1 font-display text-2xl font-semibold text-ink">Platform</h1>
        <p className="mb-8 text-sm text-ink-muted">Sign in to continue.</p>
        <form onSubmit={handleSubmit} className="space-y-3">
          <input
            type="email"
            placeholder="Email"
            autoComplete="username"
            className="w-full border border-line bg-white px-3 py-2 text-sm focus:border-ink focus:outline-none"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <input
            type="password"
            placeholder="Password"
            autoComplete="current-password"
            className="w-full border border-line bg-white px-3 py-2 text-sm focus:border-ink focus:outline-none"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {error && <p className="text-sm text-clay">{error}</p>}
          <button
            type="submit"
            disabled={submitting}
            className="w-full bg-ink px-4 py-2 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
          >
            {submitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  );
}
