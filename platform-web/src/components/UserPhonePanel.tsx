import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { UserSummaryDto } from '../types';

/**
 * A user's WhatsApp number (Users page): set, change or clear it, and send a sample
 * reminder to check that messages actually arrive. The server accepts 05XXXXXXXX or the
 * international form and stores it as international digits.
 */
export function UserPhonePanel({
  token,
  user,
  onSaved,
  onClose,
}: {
  token: string;
  user: UserSummaryDto;
  onSaved: () => Promise<void> | void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [phone, setPhone] = useState(user.phoneNumber ? `+${user.phoneNumber}` : '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useErrorMessage();
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; error: string | null } | null>(null);

  async function handleSave() {
    setError(null);
    setTestResult(null);
    setSaving(true);
    try {
      await api.users.setPhoneNumber(token, user.id, phone.trim() || null);
      await onSaved();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.phoneError' });
    } finally {
      setSaving(false);
    }
  }

  async function handleTest() {
    setError(null);
    setTestResult(null);
    setTesting(true);
    try {
      setTestResult(await api.users.sendWhatsAppTest(token, user.id));
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.phoneTestError' });
    } finally {
      setTesting(false);
    }
  }

  return (
    <div className="space-y-2 rounded border-t border-border bg-bg p-3">
      <p className="text-xs text-ink-soft">{t('userManagement.phoneExplain', { name: user.displayName })}</p>
      <div className="flex flex-wrap gap-2">
        <input
          type="tel"
          dir="ltr"
          autoComplete="off"
          aria-label={t('userManagement.phone')}
          placeholder="05XXXXXXXX"
          className="min-w-0 flex-1 rounded border border-border bg-panel px-3 py-1.5 font-mono text-sm text-ink focus:border-accent focus:outline-none"
          value={phone}
          onChange={(e) => setPhone(e.target.value)}
        />
        <button
          type="button"
          onClick={handleSave}
          disabled={saving}
          className="rounded bg-accent px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
        >
          {saving ? t('userManagement.saving') : t('common.save')}
        </button>
      </div>
      {error && <p className="text-sm text-danger">{error}</p>}
      {testResult && (
        <p className={`text-sm ${testResult.success ? 'text-success' : 'text-danger'}`} dir="auto">
          {testResult.success ? t('userManagement.phoneTestSent') : `${t('userManagement.phoneTestFailed')} ${testResult.error ?? ''}`}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {user.phoneNumber && (
          <button
            type="button"
            onClick={handleTest}
            disabled={testing}
            className="rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:border-ink disabled:opacity-50"
          >
            {testing ? t('userManagement.phoneTesting') : t('userManagement.phoneTest')}
          </button>
        )}
        <button type="button" onClick={onClose} className="px-3 py-1.5 text-sm text-ink-soft">
          {t('userManagement.close')}
        </button>
      </div>
    </div>
  );
}
