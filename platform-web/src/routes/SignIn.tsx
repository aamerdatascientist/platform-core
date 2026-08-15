import { useState } from 'react';
import { api, ApiError } from '../api/client';
import { setTokens } from '../auth/tokenStore';
import { LanguageToggle } from '../components/LanguageToggle';
import { Logo } from '../components/Logo';
import { LoadingSpinner } from '../components/LoadingSpinner';

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
    <div className="relative flex min-h-screen items-center justify-center bg-paper px-4">
      {/* Fixed physical corner, not logical (end-4) - deliberate exception to this app's
          RTL pattern, so the language switcher stays in the same spot regardless of
          which language is currently active. */}
      <div className="absolute right-4 top-4">
        <LanguageToggle tone="light" />
      </div>
      <div className="flex w-full max-w-sm flex-col items-center border border-line px-10 py-12">
        <Logo size="lg" />
        <span className="mb-1 mt-4 font-display text-lg font-semibold tracking-wide text-ink">ASAS</span>
        <p className="mb-7 text-sm text-ink-muted">Sign in to continue</p>
        <form onSubmit={handleSubmit} className="w-full space-y-3">
          <input
            type="email"
            placeholder="Email"
            autoComplete="username"
            className="w-full border border-line bg-white px-3 py-2 text-sm focus:border-signal focus:outline-none"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <input
            type="password"
            placeholder="Password"
            autoComplete="current-password"
            className="w-full border border-line bg-white px-3 py-2 text-sm focus:border-signal focus:outline-none"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {error && <p className="text-sm text-clay">{error}</p>}
          <button
            type="submit"
            disabled={submitting}
            className="flex w-full items-center justify-center gap-2 bg-signal px-4 py-2 text-sm font-medium text-white transition-all hover:-translate-y-px hover:opacity-90 disabled:opacity-50"
          >
            {submitting && <LoadingSpinner size="sm" tone="light" />}
            {submitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  );
}
