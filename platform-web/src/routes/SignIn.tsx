import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api/client';
import { setTokens } from '../auth/tokenStore';
import { Icon, LogoMark } from '../components/icons';
import { Button, FieldShell, inputClass } from '../components/ui';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { getMode, onModeChanged, setMode } from '../theme/mode';

/**
 * Sign in (screen 01): the language and light/dark buttons at the top, the logo and app name in
 * the middle, email and password with show/hide, and the Sign in button pinned at the bottom
 * (thumb zone). On a desktop the same column, 400px wide, centred. Same API call and the same
 * signIn.* messages as before (401 = wrong credentials; 429 through its error code).
 */
export function SignIn() {
  const { t, i18n } = useTranslation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useErrorMessage();
  const [submitting, setSubmitting] = useState(false);
  const [mode, setModeState] = useState(getMode());
  useEffect(() => onModeChanged(setModeState), []);
  const arabic = i18n.language.startsWith('ar');

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const tokens = await api.auth.login(email, password);
      setTokens(tokens);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) setError({ key: 'signIn.wrongCredentials' });
      else setError({ err, fallbackKey: 'signIn.genericError' });
    } finally {
      setSubmitting(false);
    }
  }

  const pillButton = 'flex h-[44px] items-center justify-center rounded-ui-pill border border-border bg-panel text-ink hover:border-ink-soft';

  return (
    <div className="flex min-h-[100dvh] justify-center bg-bg">
      <form
        onSubmit={handleSubmit}
        noValidate
        className="flex w-full max-w-[448px] flex-col px-6 pb-8 pt-5 lg:min-h-0 lg:justify-center"
        style={{ paddingBottom: 'calc(32px + env(safe-area-inset-bottom))' }}
      >
        <div className="flex items-center justify-between">
          <button type="button" onClick={() => void i18n.changeLanguage(arabic ? 'en' : 'ar')} className={`${pillButton} gap-2 px-4 text-[15px] font-medium`} lang={arabic ? 'en' : 'ar'}>
            <Icon name="globe" size={18} />
            {arabic ? t('settingsV2.languageEnglish') : t('settingsV2.languageArabic')}
          </button>
          <button
            type="button"
            onClick={() => setMode(mode === 'dark' ? 'light' : 'dark')}
            aria-label={mode === 'dark' ? t('common.lightMode') : t('common.darkMode')}
            title={mode === 'dark' ? t('common.lightMode') : t('common.darkMode')}
            className={`${pillButton} w-[44px]`}
          >
            <Icon name={mode === 'dark' ? 'sun' : 'moon'} size={20} />
          </button>
        </div>

        <div className="flex flex-1 flex-col justify-center gap-9 py-8 lg:flex-none">
          <div className="flex flex-col items-center gap-[14px]">
            <LogoMark size={72} />
            <div className="flex flex-col items-center gap-1">
              <h1 className="text-[30px] font-extrabold text-ink">{t('nav.appName')}</h1>
              <p className="text-[16px] text-ink-soft">{t('signIn.subtitle')}</p>
            </div>
          </div>

          <div className="flex flex-col gap-[18px]">
            <FieldShell label={<span className="font-medium">{t('signIn.emailPlaceholder')}</span>}>
              {({ inputId }) => (
                <input
                  id={inputId}
                  type="email"
                  dir="ltr"
                  autoComplete="username"
                  inputMode="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className={`${inputClass(false)} !h-[54px] px-4 text-start rtl:text-right`}
                />
              )}
            </FieldShell>
            <FieldShell label={<span className="font-medium">{t('signIn.passwordPlaceholder')}</span>}>
              {({ inputId }) => (
                <div className="relative">
                  <input
                    id={inputId}
                    type={showPassword ? 'text' : 'password'}
                    autoComplete="current-password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    className={`${inputClass(false)} !h-[54px] pe-14 ps-4`}
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((s) => !s)}
                    aria-label={showPassword ? t('steps.hidePassword') : t('steps.showPassword')}
                    aria-pressed={showPassword}
                    className="absolute end-1.5 top-[5px] flex h-[44px] w-[44px] items-center justify-center text-ink-soft hover:text-ink"
                  >
                    <Icon name={showPassword ? 'eyeOff' : 'eye'} size={20} />
                  </button>
                </div>
              )}
            </FieldShell>
            <p className="text-[14px] leading-relaxed text-ink-soft">{t('settingsV2.signInHint')}</p>
          </div>
        </div>

        {error && (
          <p role="alert" className="mb-3 flex items-start gap-1.5 rounded-ui-lg bg-danger-soft px-3 py-2 text-[14px] text-danger">
            <Icon name="alert" size={16} className="mt-0.5" />
            <span>{error}</span>
          </p>
        )}
        <Button type="submit" block disabled={submitting}>
          {submitting ? t('signIn.signingIn') : t('signIn.signIn')}
        </Button>
      </form>
    </div>
  );
}
