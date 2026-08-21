import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { setTokens } from '../auth/tokenStore';
import { LanguageToggle } from '../components/LanguageToggle';
import { Logo } from '../components/Logo';
import { LoadingSpinner } from '../components/LoadingSpinner';
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
        <p className="mb-7 text-sm text-ink-muted">{t('signIn.subtitle')}</p>
        <form onSubmit={handleSubmit} className="w-full space-y-3">
          <input
            type="email"
            placeholder={t('signIn.emailPlaceholder')}
            autoComplete="username"
            className="w-full border border-line bg-white px-3 py-2 text-sm focus:border-signal focus:outline-none"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <input
            type="password"
            placeholder={t('signIn.passwordPlaceholder')}
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
            {submitting ? t('signIn.signingIn') : t('signIn.signIn')}
          </button>
        </form>
      </div>
    </div>
  );
}
