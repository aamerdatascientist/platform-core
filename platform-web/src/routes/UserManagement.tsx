import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { RoleDto, UserSummaryDto } from '../types';
import { LoadingSpinner } from '../components/LoadingSpinner';

interface UserManagementProps {
  token: string;
}

export function UserManagement({ token }: UserManagementProps) {
  const [users, setUsers] = useState<UserSummaryDto[] | null>(null);
  const [roles, setRoles] = useState<RoleDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [creatingUser, setCreatingUser] = useState(false);
  const [newEmail, setNewEmail] = useState('');
  const [newDisplayName, setNewDisplayName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newUserRoleId, setNewUserRoleId] = useState('');
  const [submittingUser, setSubmittingUser] = useState(false);

  const [editingRolesFor, setEditingRolesFor] = useState<string | null>(null);
  const [pendingRoleIds, setPendingRoleIds] = useState<Set<string>>(new Set());
  const [savingRoles, setSavingRoles] = useState(false);

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
      setError(err instanceof ApiError ? err.message : 'Could not load users and roles.');
    }
  }

  async function handleCreateUser(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!newEmail.trim() || !newDisplayName.trim() || newPassword.length < 10 || !newUserRoleId) {
      setError('All fields are required, and password needs to be at least 10 characters.');
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
      setError(err instanceof ApiError ? err.message : 'Could not create that user.');
    } finally {
      setSubmittingUser(false);
    }
  }

  async function handleToggleActive(user: UserSummaryDto) {
    setError(null);
    try {
      await api.users.setActiveStatus(token, user.id, !user.isActive);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update that user.');
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
      setError(err instanceof ApiError ? err.message : 'Could not update roles for that user.');
    } finally {
      setSavingRoles(false);
    }
  }

  async function handleCreateRole(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!newRoleName.trim()) {
      setError('Role name is required.');
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
      setError(err instanceof ApiError ? err.message : 'Could not create that role.');
    } finally {
      setSubmittingRole(false);
    }
  }

  const inputClass = 'w-full border border-line px-2 py-1.5 text-sm focus:border-signal focus:outline-none';

  return (
    <div className="max-w-3xl space-y-10">
      <div>
        <h2 className="font-display text-xl font-semibold text-ink">Users &amp; roles</h2>
        <p className="text-sm text-ink-muted">
          Create accounts and assign roles - this is the only way to create a user now, Swagger's public
          registration is closed. Set up the roles you need first, then create users against them.
        </p>
      </div>

      {error && <p className="text-sm text-clay">{error}</p>}

      <div>
        <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-muted">Roles</h3>
        <div className="mb-3 space-y-1">
          {(roles ?? []).map((r) => (
            <div key={r.id} className="flex items-center justify-between border border-line bg-white px-3 py-2 text-sm">
              <span>
                <span className="font-medium text-ink">{r.name}</span>
                {r.description && <span className="ml-2 text-xs text-ink-muted">{r.description}</span>}
              </span>
              {r.isSystemRole && <span className="text-[10px] uppercase tracking-wide text-ink-muted">System</span>}
            </div>
          ))}
        </div>

        {!creatingRole ? (
          <button
            onClick={() => setCreatingRole(true)}
            className="border border-line bg-white px-3 py-1.5 text-sm text-ink hover:border-ink"
          >
            + New role
          </button>
        ) : (
          <form onSubmit={handleCreateRole} className="space-y-3 border border-line bg-white p-4">
            <div>
              <label className="mb-1 block text-xs text-ink-muted">Role name</label>
              <input className={inputClass} value={newRoleName} onChange={(e) => setNewRoleName(e.target.value)} placeholder="Site Manager" />
            </div>
            <div>
              <label className="mb-1 block text-xs text-ink-muted">Description (optional)</label>
              <input className={inputClass} value={newRoleDescription} onChange={(e) => setNewRoleDescription(e.target.value)} />
            </div>
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={submittingRole}
                className="bg-signal px-3 py-1.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submittingRole ? 'Creating…' : 'Create role'}
              </button>
              <button type="button" onClick={() => setCreatingRole(false)} className="px-3 py-1.5 text-sm text-ink-muted">
                Cancel
              </button>
            </div>
          </form>
        )}
      </div>

      <div className="border-t border-line pt-6">
        <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-muted">Users</h3>
        {!creatingUser ? (
          <button
            onClick={() => setCreatingUser(true)}
            className="border border-line bg-white px-3 py-1.5 text-sm text-ink hover:border-ink"
          >
            + New user
          </button>
        ) : (
          <form onSubmit={handleCreateUser} className="space-y-3 border border-line bg-white p-4">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label className="mb-1 block text-xs text-ink-muted">Email</label>
                <input className={inputClass} type="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} />
              </div>
              <div>
                <label className="mb-1 block text-xs text-ink-muted">Display name</label>
                <input className={inputClass} value={newDisplayName} onChange={(e) => setNewDisplayName(e.target.value)} />
              </div>
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label className="mb-1 block text-xs text-ink-muted">Password (min. 10 characters)</label>
                <input className={inputClass} type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
              </div>
              <div>
                <label className="mb-1 block text-xs text-ink-muted">Initial role</label>
                <select className={inputClass} value={newUserRoleId} onChange={(e) => setNewUserRoleId(e.target.value)}>
                  <option value="">Select…</option>
                  {(roles ?? []).map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.name}
                    </option>
                  ))}
                </select>
                {(roles?.length ?? 0) <= 1 && (
                  <p className="mt-1 text-xs text-ink-muted">
                    Only Administrator exists so far - add a role above first if you need something else.
                  </p>
                )}
              </div>
            </div>
            <p className="text-xs text-ink-muted">You can add more roles after the account is created.</p>
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={submittingUser}
                className="bg-signal px-3 py-1.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submittingUser ? 'Creating…' : 'Create user'}
              </button>
              <button type="button" onClick={() => setCreatingUser(false)} className="px-3 py-1.5 text-sm text-ink-muted">
                Cancel
              </button>
            </div>
          </form>
        )}
      </div>

      <div>
        {!users ? (
          <div className="flex items-center gap-2">
            <LoadingSpinner size="sm" />
            <span className="text-xs uppercase tracking-wide text-ink-muted">Loading…</span>
          </div>
        ) : (
          <div className="space-y-1">
            {users.map((u) => (
              <div key={u.id} className="border border-line bg-white">
                <div className="flex items-center justify-between px-3 py-2">
                  <div>
                    <span className="text-sm font-medium text-ink">{u.displayName}</span>
                    <span className="ml-2 text-xs text-ink-muted">{u.email}</span>
                    <div className="mt-0.5 flex flex-wrap gap-1">
                      {u.roles.length === 0 ? (
                        <span className="text-xs text-ink-muted">No roles</span>
                      ) : (
                        u.roles.map((r) => (
                          <span key={r.id} className="border border-line px-1.5 py-0.5 text-[10px] uppercase tracking-wide text-ink-muted">
                            {r.name}
                          </span>
                        ))
                      )}
                    </div>
                  </div>
                  <div className="flex shrink-0 items-center gap-3">
                    <span className={`text-[11px] uppercase tracking-wider ${u.isActive ? 'text-moss' : 'text-clay'}`}>
                      {u.isActive ? 'Active' : 'Deactivated'}
                    </span>
                    <button
                      onClick={() => (editingRolesFor === u.id ? setEditingRolesFor(null) : startEditingRoles(u))}
                      className="text-[11px] uppercase tracking-wide text-ink-muted hover:text-ink"
                    >
                      Edit roles
                    </button>
                    <button
                      onClick={() => handleToggleActive(u)}
                      className={`text-[11px] uppercase tracking-wide hover:opacity-70 ${u.isActive ? 'text-clay' : 'text-moss'}`}
                    >
                      {u.isActive ? 'Deactivate' : 'Reactivate'}
                    </button>
                  </div>
                </div>

                {editingRolesFor === u.id && (
                  <div className="border-t border-line bg-paper p-3">
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
                        className="bg-ink px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
                      >
                        {savingRoles ? 'Saving…' : 'Save roles'}
                      </button>
                      <button onClick={() => setEditingRolesFor(null)} className="px-3 py-1.5 text-sm text-ink-muted">
                        Cancel
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
