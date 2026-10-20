import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import type { RoleDto, UserSummaryDto } from '../types';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';

interface UserManagementProps {
  token: string;
}

export function UserManagement({ token }: UserManagementProps) {
  const { t } = useTranslation();
  const [users, setUsers] = useState<UserSummaryDto[] | null>(null);
  const [roles, setRoles] = useState<RoleDto[] | null>(null);
  const [error, setError] = useErrorMessage();

  const [creatingUser, setCreatingUser] = useState(false);
  const [newEmail, setNewEmail] = useState('');
  const [newDisplayName, setNewDisplayName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newUserRoleId, setNewUserRoleId] = useState('');
  const [submittingUser, setSubmittingUser] = useState(false);

  const [editingRolesFor, setEditingRolesFor] = useState<string | null>(null);
  const [pendingRoleIds, setPendingRoleIds] = useState<Set<string>>(new Set());
  const [savingRoles, setSavingRoles] = useState(false);

  // Password reset for someone who forgot theirs: one open panel at a time.
  const [resettingFor, setResettingFor] = useState<string | null>(null);
  const [resetPassword, setResetPassword] = useState('');
  const [submittingReset, setSubmittingReset] = useState(false);
  const [resetDoneFor, setResetDoneFor] = useState<string | null>(null);
  const [resetError, setResetError] = useErrorMessage();

  const [creatingRole, setCreatingRole] = useState(false);
  const [newRoleName, setNewRoleName] = useState('');
  const [newRoleDescription, setNewRoleDescription] = useState('');
  const [submittingRole, setSubmittingRole] = useState(false);

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function load() {
    try {
      const [u, r] = await Promise.all([api.users.list(token), api.roles.list(token)]);
      setUsers(u);
      setRoles(r);
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.loadError' });
    }
  }

  async function handleCreateUser(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!newEmail.trim() || !newDisplayName.trim() || newPassword.length < 10 || !newUserRoleId) {
      setError({ key: 'userManagement.createUserRequiredError' });
      return;
    }
    setSubmittingUser(true);
    try {
      await api.auth.register(token, {
        email: newEmail,
        displayName: newDisplayName,
        password: newPassword,
        departmentId: null,
        defaultRoleId: newUserRoleId,
      });
      setNewEmail('');
      setNewDisplayName('');
      setNewPassword('');
      setNewUserRoleId('');
      setCreatingUser(false);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.createUserError' });
    } finally {
      setSubmittingUser(false);
    }
  }

  function startReset(userId: string) {
    setResettingFor(resettingFor === userId ? null : userId);
    setResetPassword('');
    setResetDoneFor(null);
    setResetError(null);
  }

  /** 12 characters without look-alikes (0/O, 1/l/I), easy to read out over the phone. */
  function generatePassword() {
    const alphabet = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    const bytes = new Uint32Array(12);
    crypto.getRandomValues(bytes);
    setResetPassword(Array.from(bytes, (b) => alphabet[b % alphabet.length]).join(''));
  }

  async function handleResetPassword(userId: string) {
    setResetError(null);
    if (resetPassword.length < 10) {
      setResetError({ key: 'userManagement.resetTooShort' });
      return;
    }
    setSubmittingReset(true);
    try {
      await api.users.resetPassword(token, userId, resetPassword);
      setResetDoneFor(userId);
    } catch (err) {
      setResetError({ err, fallbackKey: 'userManagement.resetError' });
    } finally {
      setSubmittingReset(false);
    }
  }

  async function handleToggleActive(user: UserSummaryDto) {
    setError(null);
    try {
      await api.users.setActiveStatus(token, user.id, !user.isActive);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.toggleActiveError' });
    }
  }

  function startEditingRoles(user: UserSummaryDto) {
    setEditingRolesFor(user.id);
    setPendingRoleIds(new Set(user.roles.map((r) => r.id)));
  }

  function toggleRoleInEditor(roleId: string) {
    setPendingRoleIds((prev) => {
      const next = new Set(prev);
      if (next.has(roleId)) next.delete(roleId);
      else next.add(roleId);
      return next;
    });
  }

  async function handleSaveRoles(userId: string) {
    setSavingRoles(true);
    setError(null);
    try {
      await api.users.setRoles(token, userId, [...pendingRoleIds]);
      setEditingRolesFor(null);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.saveRolesError' });
    } finally {
      setSavingRoles(false);
    }
  }

  async function handleCreateRole(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!newRoleName.trim()) {
      setError({ key: 'userManagement.roleNameRequired' });
      return;
    }
    setSubmittingRole(true);
    try {
      await api.roles.create(token, newRoleName, newRoleDescription || undefined);
      setNewRoleName('');
      setNewRoleDescription('');
      setCreatingRole(false);
      await load();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.createRoleError' });
    } finally {
      setSubmittingRole(false);
    }
  }

  const inputClass = 'w-full border border-border rounded bg-bg px-2 py-1.5 text-sm focus:border-accent focus:outline-none';

  return (
    <div className="max-w-3xl space-y-10">
      <div>
        <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{t('userManagement.title')}</h2>
        <p className="text-sm text-ink-soft">{t('userManagement.subtitle')}</p>
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      <div>
        <h3 className="mb-3 text-label font-medium uppercase tracking-wider text-ink-soft">{t('userManagement.roles')}</h3>
        <div className="mb-3 space-y-1">
          {(roles ?? []).map((r) => (
            <div key={r.id} className="flex items-center justify-between border border-border rounded bg-panel px-3 py-2 text-sm">
              <span>
                <span className="font-medium text-ink">{r.name}</span>
                {r.description && <span className="ms-2 text-xs text-ink-soft">{r.description}</span>}
              </span>
              {r.isSystemRole && <span className="text-micro uppercase tracking-wide text-ink-soft">{t('userManagement.system')}</span>}
            </div>
          ))}
        </div>

        {!creatingRole ? (
          <button
            onClick={() => setCreatingRole(true)}
            className="border border-border rounded bg-panel px-3 py-1.5 text-sm text-ink hover:border-ink"
          >
            {t('userManagement.newRole')}
          </button>
        ) : (
          <form onSubmit={handleCreateRole} className="space-y-3 border border-border rounded bg-panel p-4 shadow-recessed">
            <div>
              <label className="mb-1 block text-xs text-ink-soft">{t('userManagement.roleName')}</label>
              <input
                className={inputClass}
                value={newRoleName}
                onChange={(e) => setNewRoleName(e.target.value)}
                placeholder={t('userManagement.roleNamePlaceholder')}
              />
            </div>
            <div>
              <label className="mb-1 block text-xs text-ink-soft">{t('userManagement.descriptionOptional')}</label>
              <input className={inputClass} value={newRoleDescription} onChange={(e) => setNewRoleDescription(e.target.value)} />
            </div>
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={submittingRole}
                className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submittingRole ? t('userManagement.creating') : t('userManagement.createRole')}
              </button>
              <button type="button" onClick={() => setCreatingRole(false)} className="px-3 py-1.5 text-sm text-ink-soft">
                {t('common.cancel')}
              </button>
            </div>
          </form>
        )}
      </div>

      <div className="border-t border-border rounded pt-6">
        <h3 className="mb-3 text-label font-medium uppercase tracking-wider text-ink-soft">{t('userManagement.users')}</h3>
        {!creatingUser ? (
          <button
            onClick={() => setCreatingUser(true)}
            className="border border-border rounded bg-panel px-3 py-1.5 text-sm text-ink hover:border-ink"
          >
            {t('userManagement.newUser')}
          </button>
        ) : (
          <form onSubmit={handleCreateUser} className="space-y-3 border border-border rounded bg-panel p-4 shadow-recessed">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label className="mb-1 block text-xs text-ink-soft">{t('userManagement.email')}</label>
                <input className={inputClass} type="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} />
              </div>
              <div>
                <label className="mb-1 block text-xs text-ink-soft">{t('userManagement.displayName')}</label>
                <input className={inputClass} value={newDisplayName} onChange={(e) => setNewDisplayName(e.target.value)} />
              </div>
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label className="mb-1 block text-xs text-ink-soft">{t('userManagement.passwordMinLength')}</label>
                <input className={inputClass} type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
              </div>
              <div>
                <label className="mb-1 block text-xs text-ink-soft">{t('userManagement.initialRole')}</label>
                <select className={inputClass} value={newUserRoleId} onChange={(e) => setNewUserRoleId(e.target.value)}>
                  <option value="">{t('common.select')}</option>
                  {(roles ?? []).map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.name}
                    </option>
                  ))}
                </select>
                {(roles?.length ?? 0) <= 1 && (
                  <p className="mt-1 text-xs text-ink-soft">{t('userManagement.onlyAdminExists')}</p>
                )}
              </div>
            </div>
            <p className="text-xs text-ink-soft">{t('userManagement.canAddMoreRolesLater')}</p>
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={submittingUser}
                className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submittingUser ? t('userManagement.creating') : t('userManagement.createUser')}
              </button>
              <button type="button" onClick={() => setCreatingUser(false)} className="px-3 py-1.5 text-sm text-ink-soft">
                {t('common.cancel')}
              </button>
            </div>
          </form>
        )}
      </div>

      <div>
        {!users ? (
          <div className="flex items-center gap-2">
            <LoadingSpinner size="sm" />
            <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
          </div>
        ) : (
          <div className="space-y-1">
            {users.map((u) => (
              <div key={u.id} className="border border-border rounded bg-panel shadow-recessed">
                <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
                  <div>
                    <span className="text-sm font-medium text-ink">{u.displayName}</span>
                    <span className="ms-2 text-xs text-ink-soft">{u.email}</span>
                    <div className="mt-0.5 flex flex-wrap gap-1">
                      {u.roles.length === 0 ? (
                        <span className="text-xs text-ink-soft">{t('userManagement.noRoles')}</span>
                      ) : (
                        u.roles.map((r) => (
                          <span key={r.id} className="border border-border rounded px-1.5 py-0.5 text-micro uppercase tracking-wide text-ink-soft">
                            {r.name}
                          </span>
                        ))
                      )}
                    </div>
                  </div>
                  <div className="flex flex-wrap items-center gap-3">
                    <span className={`text-label uppercase tracking-wider ${u.isActive ? 'text-success' : 'text-danger'}`}>
                      {u.isActive ? t('userManagement.active') : t('userManagement.deactivated')}
                    </span>
                    <button
                      onClick={() => (editingRolesFor === u.id ? setEditingRolesFor(null) : startEditingRoles(u))}
                      className="text-label uppercase tracking-wide text-ink-soft hover:text-ink"
                    >
                      {t('userManagement.editRoles')}
                    </button>
                    <button
                      onClick={() => startReset(u.id)}
                      className="text-label uppercase tracking-wide text-ink-soft hover:text-ink"
                    >
                      {t('userManagement.resetPassword')}
                    </button>
                    <button
                      onClick={() => handleToggleActive(u)}
                      className={`text-label uppercase tracking-wide hover:opacity-70 ${u.isActive ? 'text-danger' : 'text-success'}`}
                    >
                      {u.isActive ? t('userManagement.deactivate') : t('userManagement.reactivate')}
                    </button>
                  </div>
                </div>

                {resettingFor === u.id && (
                  <div className="border-t border-border rounded bg-bg p-3">
                    {resetDoneFor === u.id ? (
                      <div className="space-y-2">
                        <p className="text-sm text-ink" role="status">
                          {t('userManagement.resetDone', { name: u.displayName })}
                        </p>
                        <p dir="ltr" className="font-mono text-sm text-ink">{resetPassword}</p>
                        <button onClick={() => setResettingFor(null)} className="px-3 py-1.5 text-sm text-ink-soft">
                          {t('userManagement.close')}
                        </button>
                      </div>
                    ) : (
                      <div className="space-y-2">
                        <p className="text-xs text-ink-soft">{t('userManagement.resetExplain', { name: u.displayName })}</p>
                        <div className="flex flex-wrap gap-2">
                          <input
                            type="text"
                            dir="ltr"
                            autoComplete="off"
                            spellCheck={false}
                            aria-label={t('userManagement.newPassword')}
                            placeholder={t('userManagement.newPassword')}
                            className="min-w-0 flex-1 rounded border border-border bg-panel px-3 py-1.5 font-mono text-sm text-ink focus:border-accent focus:outline-none"
                            value={resetPassword}
                            onChange={(e) => setResetPassword(e.target.value)}
                          />
                          <button
                            type="button"
                            onClick={generatePassword}
                            className="rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:bg-bg"
                          >
                            {t('userManagement.generate')}
                          </button>
                        </div>
                        {resetError && <p className="text-sm text-danger">{resetError}</p>}
                        <div className="flex gap-2">
                          <button
                            onClick={() => handleResetPassword(u.id)}
                            disabled={submittingReset}
                            className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
                          >
                            {submittingReset ? t('userManagement.saving') : t('userManagement.setPassword')}
                          </button>
                          <button onClick={() => setResettingFor(null)} className="px-3 py-1.5 text-sm text-ink-soft">
                            {t('common.cancel')}
                          </button>
                        </div>
                      </div>
                    )}
                  </div>
                )}

                {editingRolesFor === u.id && (
                  <div className="border-t border-border rounded bg-bg p-3">
                    <div className="mb-3 flex flex-wrap gap-3">
                      {(roles ?? []).map((r) => (
                        <label key={r.id} className="flex items-center gap-1.5 text-sm text-ink">
                          <input type="checkbox" checked={pendingRoleIds.has(r.id)} onChange={() => toggleRoleInEditor(r.id)} />
                          {r.name}
                        </label>
                      ))}
                    </div>
                    <div className="flex gap-2">
                      <button
                        onClick={() => handleSaveRoles(u.id)}
                        disabled={savingRoles}
                        className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
                      >
                        {savingRoles ? t('userManagement.saving') : t('userManagement.saveRoles')}
                      </button>
                      <button onClick={() => setEditingRolesFor(null)} className="px-3 py-1.5 text-sm text-ink-soft">
                        {t('common.cancel')}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
