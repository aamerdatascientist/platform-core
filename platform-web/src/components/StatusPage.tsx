import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type { StatusKind } from '../api/errorMessage';
import { LanguageToggle } from './LanguageToggle';
import { Logo } from './Logo';
import { ModeToggle } from './ModeToggle';

const STATUS_CODE: Record<StatusKind, string | null> = {
  sessionEnded: '401',
  forbidden: '403',
  notFound: '404',
  serverError: '500',
  offline: null,
};

interface StatusPageProps {
  kind: StatusKind;
  /** Only for 'sessionEnded': what "Sign in again" does. */
  onSignIn?: () => void;
}

/**
 * One plain-language page for the errors that stop a whole page: the session ended (401), no
 * access (403), nothing at this address (404), the server failing (5xx) or no connection.
 * 'sessionEnded' is shown on its own, before the sign-in form (App.tsx); the others inside the
 * app's layout. The code is shown small, so someone reporting a problem can quote it.
 */
export function StatusPage({ kind, onSignIn }: StatusPageProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const code = STATUS_CODE[kind];

  const action =
    kind === 'sessionEnded' ? (
      <button type="button" onClick={onSignIn} className={primaryButton}>
        {t('errors.page.signInAgain')}
      </button>
    ) : kind === 'serverError' || kind === 'offline' ? (
      <button type="button" onClick={() => window.location.reload()} className={primaryButton}>
        {t('errors.page.tryAgain')}
      </button>
    ) : (
      <button type="button" onClick={() => navigate('/')} className={primaryButton}>
        {t('errors.page.goHome')}
      </button>
    );

  const card = (
    <div role="alert" className="flex w-full max-w-md flex-col items-center rounded border border-border bg-panel px-8 py-10 text-center shadow-recessed">
      {code && <p className="font-display text-xs font-semibold uppercase tracking-[0.1em] text-ink-soft">{t('errors.page.code', { code })}</p>}
      <h1 className="mt-2 font-display text-lg font-semibold text-ink">{t(`errors.page.${kind}.title`)}</h1>
      <p className="mt-3 text-sm leading-relaxed text-ink-soft">{t(`errors.page.${kind}.body`)}</p>
      <div className="mt-6">{action}</div>
    </div>
  );

  if (kind !== 'sessionEnded') {
    return <div className="flex min-h-[60vh] items-center justify-center px-4">{card}</div>;
  }

  // Shown instead of the sign-in form, so it carries the same frame: logo, language and mode.
  return (
    <div className="signin-bg relative flex min-h-screen flex-col items-center justify-center gap-6 px-4">
      <div dir="ltr" className="absolute right-4 top-4 flex flex-wrap items-center justify-end gap-x-3 gap-y-2">
        <LanguageToggle tone="light" />
        <ModeToggle tone="light" />
      </div>
      <Logo size="lg" />
      {card}
    </div>
  );
}

const primaryButton =
  'rounded bg-accent px-4 py-2 text-sm font-medium text-accent-ink transition-all hover:-translate-y-px hover:opacity-90';
