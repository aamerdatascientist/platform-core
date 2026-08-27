import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { setTokens } from '../auth/tokenStore';
import { LanguageToggle } from '../components/LanguageToggle';
import { Logo } from '../components/Logo';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { ModeToggle } from '../components/ModeToggle';
import { useErrorMessage } from '../hooks/useErrorMessage';

export function SignIn() {
  const { t } = useTranslation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useErrorMessage();
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const tokens = await api.auth.login(email, password);
      setTokens(tokens);
    } catch (err) {
      setError({ err, fallbackKey: 'signIn.genericError' });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="signin-bg relative flex min-h-screen items-center justify-center px-4">
      {/* Fixed physical corner, not logical (end-4) - deliberate exception to this app's
          RTL pattern, so the language switcher stays in the same spot regardless of
          which language is currently active. */}
      <div className="absolute right-4 top-4 flex items-center gap-4">
        <LanguageToggle tone="light" />
        <ModeToggle tone="light" />
      </div>
      <div className="flex w-full max-w-sm flex-col items-center border border-border rounded bg-panel py-12 shadow-recessed">
        <div className="flex flex-col items-center px-10">
          <Logo size="lg" />
          <span className="mb-1 mt-4 font-display text-lg font-semibold uppercase tracking-[0.07em] text-ink">ASAS</span>
          <p className="text-sm text-ink-soft">{t('signIn.subtitle')}</p>
        </div>
        <div className="rivet-strip my-7 w-full" />
        <form onSubmit={handleSubmit} className="w-full space-y-3 px-10">
          <input
            type="email"
            placeholder={t('signIn.emailPlaceholder')}
            autoComplete="username"
            className="w-full border border-border rounded bg-panel px-3 py-2 text-sm focus:border-accent focus:outline-none"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <input
            type="password"
            placeholder={t('signIn.passwordPlaceholder')}
            autoComplete="current-password"
            className="w-full border border-border rounded bg-panel px-3 py-2 text-sm focus:border-accent focus:outline-none"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {error && <p className="text-sm text-danger">{error}</p>}
          <button
            type="submit"
            disabled={submitting}
            className="flex w-full items-center justify-center gap-2 bg-accent rounded px-4 py-2 text-sm font-medium text-accent-ink transition-all hover:-translate-y-px hover:opacity-90 disabled:opacity-50"
          >
            {submitting && <LoadingSpinner size="sm" tone="light" />}
            {submitting ? t('signIn.signingIn') : t('signIn.signIn')}
          </button>
        </form>
      </div>
    </div>
  );
}
