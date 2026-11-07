import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { builderInput } from '../components/builder/FieldSettings';
import { initial, useShell } from '../components/shell/ShellData';
import { Button, ClickableRow, Drawer, ErrorState, LoadingState, Segmented, Sheet, TableCard, tdClass, thClass } from '../components/ui';
import { UserTelegramPanel } from '../components/UserTelegramPanel';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { useWhatsAppGate } from '../config/whatsapp';
import type { RoleDto, UserSummaryDto } from '../types';

type Filter = 'all' | 'active' | 'off';

/**
 * Users & roles (screen 22): role cards, a search + All / Active / Deactivated filter, the users
 * table (avatar, name, email, roles, reminders, status) and a drawer per user - roles as toggle
 * chips (saved at once; the server's self-demotion guard still applies), WhatsApp number and
 * test, Telegram status with the existing linking steps in a dialog, password reset (existing
 * flow), and deactivate / reactivate. "+ New role" and "+ New user" open the existing forms.
 */
export function UserManagement({ token }: { token: string }) {
  const { t } = useTranslation();
  const { roleLabel } = useShell();
  const [users, setUsers] = useState<UserSummaryDto[] | null>(null);
  const [roles, setRoles] = useState<RoleDto[] | null>(null);
  const [error, setError] = useErrorMessage();
  const [q, setQ] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [selected, setSelected] = useState<string | null>(null);
  const [dialog, setDialog] = useState<'none' | 'user' | 'role'>('none');

  async function load() {
    try {
      const [u, r] = await Promise.all([api.users.list(token), api.roles.list(token)]);
      setUsers(u);
      setRoles(r);
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.loadError' });
    }
  }
  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  const shown = useMemo(() => {
    const term = q.trim().toLowerCase();
    return (users ?? []).filter(
      (u) =>
        (filter === 'all' || (filter === 'active') === u.isActive) &&
        (!term || u.displayName.toLowerCase().includes(term) || u.email.toLowerCase().includes(term)),
    );
  }, [users, q, filter]);

  if (!users || !roles) return error ? <ErrorState message={error} onRetry={() => (setError(null), void load())} /> : <LoadingState />;
  const active = users.filter((u) => u.isActive).length;
  const user = users.find((u) => u.id === selected) ?? null;

  const reminders = (u: UserSummaryDto) => {
    if (!u.isActive) return <span className="text-ink-soft">—</span>;
    if (u.telegramLinked)
      return <span className="text-success">{u.phoneNumber ? t('usersV2.remindersTelegramWhatsapp') : t('usersV2.remindersTelegram')}</span>;
    if (u.phoneNumber) return <span className="text-success">{t('usersV2.remindersWhatsapp')}</span>;
    return <span className="text-warning">{t('usersV2.noTelegram')}</span>;
  };

  return (
    <div className="flex flex-col gap-[18px]">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-[14px] text-ink-soft">{t('nav.administration')}</span>
          <h1 className="text-[28px] font-extrabold text-ink">{t('userManagement.title')}</h1>
        </div>
        <div className="flex gap-2">
          <Button size="desktop" variant="secondary" className="!font-normal" onClick={() => setDialog('role')}>
            {t('usersV2.newRole')}
          </Button>
          <Button size="desktop" onClick={() => setDialog('user')}>
            {t('usersV2.newUser')}
          </Button>
        </div>
      </div>

      {error && <p className="text-[14px] text-danger">{error}</p>}

      {/* Role cards */}
      <div className="flex flex-wrap gap-[10px]">
        {roles.map((r) => {
          const count = users.filter((u) => u.roles.some((x) => x.id === r.id)).length;
          return (
            <div key={r.id} className="flex flex-[1_1_180px] flex-col gap-1 rounded-ui-xl border border-border bg-panel px-[14px] py-3">
              <div className="flex items-baseline justify-between gap-2">
                <span className="text-[15px] font-extrabold text-ink">
                  <bdi>{roleLabel(r.name)}</bdi>
                </span>
                <span className="whitespace-nowrap text-[13px] text-ink-soft">{t('usersV2.usersCount', { count })}</span>
              </div>
              <span className="text-[13px] text-ink-soft">
                <bdi>{[r.description, r.isSystemRole ? t('userManagement.system') : null].filter(Boolean).join(' · ') || '—'}</bdi>
              </span>
            </div>
          );
        })}
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-2">
        <label className="flex h-[44px] min-w-[240px] items-center lg:h-[40px] gap-2 rounded-ui-lg border border-border bg-panel px-3">
          <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t('usersV2.searchUsers')} aria-label={t('usersV2.searchUsers')} className="min-w-0 flex-1 bg-transparent text-[15px] text-ink placeholder:text-ink-soft focus:outline-none" />
        </label>
        <div className="w-[360px] max-w-full">
          <Segmented<Filter>
            size="desktop"
            label={t('usersV2.colStatus')}
            value={filter}
            onChange={setFilter}
            options={[
              { value: 'all', label: t('usersV2.filterAll', { count: users.length }) },
              { value: 'active', label: t('usersV2.filterActive', { count: active }) },
              { value: 'off', label: t('usersV2.filterDeactivated', { count: users.length - active }) },
            ]}
          />
        </div>
      </div>

      <div className="flex flex-wrap items-start gap-4">
        <div className="min-w-0 flex-[999_1_520px]">
          <TableCard className="[&_table]:min-w-[640px]">
            <thead>
              <tr>
                <th className={thClass}>{t('usersV2.colUser')}</th>
                <th className={thClass}>{t('usersV2.colRole')}</th>
                <th className={thClass}>{t('usersV2.colReminders')}</th>
                <th className={thClass}>{t('usersV2.colStatus')}</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((u) => (
                <ClickableRow key={u.id} selected={u.id === selected} onOpen={() => setSelected(u.id)} label={u.displayName}>
                  <td className={tdClass}>
                    <div className="flex items-center gap-[10px]">
                      <span aria-hidden="true" className="flex h-[34px] w-[34px] shrink-0 items-center justify-center rounded-full bg-accent-soft font-extrabold text-accent">
                        {initial(u.displayName)}
                      </span>
                      <span className="flex min-w-0 flex-col">
                        <span className="font-bold text-ink">
                          <bdi>{u.displayName}</bdi>
                        </span>
                        <span dir="ltr" className="text-start text-[12px] text-ink-soft rtl:text-right">
                          {u.email}
                        </span>
                      </span>
                    </div>
                  </td>
                  <td className={tdClass}>
                    <span className="flex flex-wrap gap-1">
                      {u.roles.length === 0 ? (
                        <span className="text-ink-soft">{t('userManagement.noRoles')}</span>
                      ) : (
                        u.roles.map((r) => (
                          <span key={r.id} className="whitespace-nowrap rounded-ui-pill border border-border bg-field-muted px-[9px] py-[3px] text-[12px] font-bold text-ink">
                            <bdi>{roleLabel(r.name)}</bdi>
                          </span>
                        ))
                      )}
                    </span>
                  </td>
                  <td className={`${tdClass} whitespace-nowrap text-[13px]`}>{reminders(u)}</td>
                  <td className={tdClass}>
                    <span className={`rounded-ui-pill px-[9px] py-[3px] text-[12px] font-bold ${u.isActive ? 'bg-success-soft text-success' : 'bg-divider text-ink-soft'}`}>
                      {u.isActive ? t('userManagement.active') : t('userManagement.deactivated')}
                    </span>
                  </td>
                </ClickableRow>
              ))}
            </tbody>
          </TableCard>
        </div>
        {user && <UserDrawer key={user.id} token={token} user={user} roles={roles} onClose={() => setSelected(null)} onChanged={load} />}
      </div>

      <Sheet open={dialog === 'user'} onClose={() => setDialog('none')} title={t('userManagement.newUser')} desktopWidth={560}>
        <NewUserForm token={token} roles={roles} onDone={() => (setDialog('none'), void load())} />
      </Sheet>
      <Sheet open={dialog === 'role'} onClose={() => setDialog('none')} title={t('userManagement.newRole')} desktopWidth={480}>
        <NewRoleForm token={token} onDone={() => (setDialog('none'), void load())} />
      </Sheet>
    </div>
  );
}

function UserDrawer({ token, user, roles, onClose, onChanged }: { token: string; user: UserSummaryDto; roles: RoleDto[]; onClose: () => void; onChanged: () => Promise<void> }) {
  const { t } = useTranslation();
  const { roleLabel } = useShell();
  const [error, setError] = useErrorMessage();
  const [busy, setBusy] = useState(false);
  const [phone, setPhone] = useState(user.phoneNumber ? `+${user.phoneNumber}` : '');
  const [note, setNote] = useState<string | null>(null);
  const [telegramOpen, setTelegramOpen] = useState(false);
  const whatsapp = useWhatsAppGate();
  const [resetOpen, setResetOpen] = useState(false);

  async function act(action: () => Promise<unknown>, fallbackKey: string, done?: string) {
    setBusy(true);
    setError(null);
    setNote(null);
    try {
      await action();
      await onChanged();
      if (done) setNote(done);
    } catch (err) {
      setError({ err, fallbackKey });
    } finally {
      setBusy(false);
    }
  }

  const toggleRole = (roleId: string) => {
    const next = user.roles.some((r) => r.id === roleId) ? user.roles.filter((r) => r.id !== roleId).map((r) => r.id) : [...user.roles.map((r) => r.id), roleId];
    void act(() => api.users.setRoles(token, user.id, next), 'userManagement.saveRolesError');
  };
  const phoneChanged = phone.replace(/^\+/, '').trim() !== (user.phoneNumber ?? '');

  return (
    <Drawer
      open
      onClose={onClose}
      leading={
        <span aria-hidden="true" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-accent-soft text-[18px] font-extrabold text-accent">
          {initial(user.displayName)}
        </span>
      }
      title={<bdi>{user.displayName}</bdi>}
      sub={
        <span dir="ltr" className="block text-start rtl:text-right">
          {user.email}
        </span>
      }
      footer={
        user.isActive ? (
          <Button size="desktop" variant="danger" disabled={busy} className="!h-[40px] !min-h-[40px] !text-[14px] !font-normal" onClick={() => act(() => api.users.setActiveStatus(token, user.id, false), 'userManagement.toggleActiveError')}>
            {t('usersV2.deactivateAccount')}
          </Button>
        ) : (
          <Button size="desktop" variant="secondary" disabled={busy} className="!h-[40px] !min-h-[40px] !text-[14px] !font-normal" onClick={() => act(() => api.users.setActiveStatus(token, user.id, true), 'userManagement.toggleActiveError')}>
            {t('usersV2.reactivateAccount')}
          </Button>
        )
      }
    >
      <section className="flex flex-col gap-2 px-[18px] py-[14px]">
        <h3 className="text-[14px] font-extrabold text-ink">{t('usersV2.rolesTitle')}</h3>
        <div className="flex flex-wrap gap-1.5">
          {roles.map((r) => {
            const on = user.roles.some((x) => x.id === r.id);
            return (
              <button
                key={r.id}
                type="button"
                aria-pressed={on}
                disabled={busy}
                onClick={() => toggleRole(r.id)}
                className={`h-[34px] rounded-ui-pill border-[1.5px] px-3 text-[13px] ${on ? 'border-accent bg-accent-soft font-bold text-accent-strong' : 'border-border bg-panel text-ink'}`}
              >
                {on ? '✓ ' : ''}
                <bdi>{roleLabel(r.name)}</bdi>
              </button>
            );
          })}
        </div>
      </section>

      <section className="flex flex-col gap-[10px] px-[18px] py-[14px]">
        <h3 className="text-[14px] font-extrabold text-ink">{t('usersV2.remindersTitle')}</h3>
        <label className="flex flex-col gap-1.5 text-[13px] text-ink-soft">
          {t('usersV2.whatsappNumber')}
          <span className="flex gap-1.5">
            <input dir="ltr" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="0551234567" className={`${builderInput} !h-[40px] text-[14px]`} />
            {phoneChanged ? (
              <Button size="compact" className="!h-[40px] !min-h-[40px] shrink-0" disabled={busy} onClick={() => act(() => api.users.setPhoneNumber(token, user.id, phone.trim() || null), 'userManagement.phoneError')}>
                {t('common.save')}
              </Button>
            ) : (
              <Button
                size="compact"
                variant="secondary"
                className="!h-[40px] !min-h-[40px] shrink-0 !border !text-[13px]"
                disabled={busy}
                onClick={() =>
                  whatsapp.guard(() =>
                    act(async () => {
                      const r = await api.users.sendWhatsAppTest(token, user.id);
                      if (!r.success) throw new Error(r.error ?? 'failed');
                    }, 'userManagement.phoneTestFailed', t('userManagement.phoneTestSent')),
                  )
                }
              >
                {t('usersV2.testMessage')}
              </Button>
            )}
          </span>
        </label>
        {/* WhatsApp, as it will be once connected (config/whatsapp.ts). */}
        {whatsapp.ready ? (
          <div className={`flex items-center gap-[10px] rounded-ui-lg px-3 py-[10px] ${user.phoneNumber ? 'bg-success-soft' : 'bg-warning-soft'}`}>
            <span className={`flex-1 text-[13px] ${user.phoneNumber ? 'text-success' : 'text-warning-strong'}`}>
              {user.phoneNumber ? t('whatsapp.statusReady') : t('whatsapp.statusNoNumber')}
            </span>
          </div>
        ) : (
          <div className="flex items-center gap-[10px] rounded-ui-lg bg-warning-soft px-3 py-[10px]">
            <span className="flex-1 text-[13px] text-warning-strong">{t('whatsapp.statusBox')}</span>
            <button type="button" onClick={() => whatsapp.guard(() => undefined)} className="h-[34px] shrink-0 rounded-[9px] bg-panel px-[10px] text-[13px] font-bold text-ink">
              {t('whatsapp.connect')}
            </button>
          </div>
        )}
        {whatsapp.notice && (
          <p role="status" className="text-[13px] text-warning-strong">
            {t('whatsapp.notReady')}
          </p>
        )}
        <div className={`flex items-center gap-[10px] rounded-ui-lg px-3 py-[10px] ${user.telegramLinked ? 'bg-success-soft' : 'bg-warning-soft'}`}>
          <span className={`flex-1 text-[13px] ${user.telegramLinked ? 'text-success-strong' : 'text-warning-strong'}`}>
            {user.telegramLinked ? t('usersV2.telegramLinkedBox') : t('usersV2.telegramNotLinkedBox')}
          </span>
          <button type="button" onClick={() => setTelegramOpen(true)} className="h-[34px] shrink-0 rounded-[9px] bg-panel px-[10px] text-[13px] font-bold text-ink">
            {user.telegramLinked ? t('userManagement.telegram') : t('usersV2.linkGroup')}
          </button>
        </div>
        {note && <p className="text-[13px] text-success" role="status">{note}</p>}
      </section>

      <section className="flex flex-col gap-2 px-[18px] py-[14px]">
        <h3 className="text-[14px] font-extrabold text-ink">{t('usersV2.passwordTitle')}</h3>
        <Button size="compact" variant="secondary" className="self-start !h-[38px] !min-h-[38px] !border" onClick={() => setResetOpen(true)}>
          {t('userManagement.resetPassword')}
        </Button>
      </section>
      {error && <p className="px-[18px] py-2 text-[14px] text-danger">{error}</p>}

      <Sheet open={telegramOpen} onClose={() => setTelegramOpen(false)} title={t('userManagement.telegram')} desktopWidth={560}>
        <UserTelegramPanel token={token} user={user} onChanged={onChanged} onClose={() => setTelegramOpen(false)} />
      </Sheet>
      <Sheet open={resetOpen} onClose={() => setResetOpen(false)} title={t('userManagement.resetPassword')} desktopWidth={480}>
        <ResetPassword token={token} user={user} onClose={() => setResetOpen(false)} />
      </Sheet>
    </Drawer>
  );
}

/** The existing password reset: type or generate, set, then show it once to read out. */
function ResetPassword({ token, user, onClose }: { token: string; user: UserSummaryDto; onClose: () => void }) {
  const { t } = useTranslation();
  const [password, setPassword] = useState('');
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();
  /** 12 characters without look-alikes (0/O, 1/l/I), easy to read out over the phone. */
  const generate = () => {
    const alphabet = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    const bytes = new Uint32Array(12);
    crypto.getRandomValues(bytes);
    setPassword(Array.from(bytes, (b) => alphabet[b % alphabet.length]).join(''));
  };
  async function submit() {
    setError(null);
    if (password.length < 10) return setError({ key: 'userManagement.resetTooShort' });
    setBusy(true);
    try {
      await api.users.resetPassword(token, user.id, password);
      setDone(true);
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.resetError' });
    } finally {
      setBusy(false);
    }
  }
  if (done)
    return (
      <div className="flex flex-col gap-3 pb-2">
        <p className="text-[14px] text-ink" role="status">
          {t('userManagement.resetDone', { name: user.displayName })}
        </p>
        <p dir="ltr" className="rounded-ui-md bg-field-muted px-3 py-2 font-mono text-[15px] text-ink">
          {password}
        </p>
        <Button size="desktop" variant="secondary" className="self-start" onClick={onClose}>
          {t('userManagement.close')}
        </Button>
      </div>
    );
  return (
    <div className="flex flex-col gap-3 pb-2">
      <p className="text-[14px] text-ink-soft">{t('userManagement.resetExplain', { name: user.displayName })}</p>
      <div className="flex gap-2">
        <input type="text" dir="ltr" autoComplete="off" spellCheck={false} aria-label={t('userManagement.newPassword')} placeholder={t('userManagement.newPassword')} className={`${builderInput} font-mono`} value={password} onChange={(e) => setPassword(e.target.value)} />
        <Button size="desktop" variant="secondary" onClick={generate}>
          {t('userManagement.generate')}
        </Button>
      </div>
      {error && <p className="text-[14px] text-danger">{error}</p>}
      <Button size="desktop" className="self-start" disabled={busy} onClick={submit}>
        {busy ? t('userManagement.saving') : t('userManagement.setPassword')}
      </Button>
    </div>
  );
}

/** The existing "new user" form: email, name, password (10+), first role. */
function NewUserForm({ token, roles, onDone }: { token: string; roles: RoleDto[]; onDone: () => void }) {
  const { t } = useTranslation();
  const { roleLabel } = useShell();
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [roleId, setRoleId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();
  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!email.trim() || !name.trim() || password.length < 10 || !roleId) return setError({ key: 'userManagement.createUserRequiredError' });
    setBusy(true);
    try {
      await api.auth.register(token, { email, displayName: name, password, departmentId: null, defaultRoleId: roleId });
      onDone();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.createUserError' });
    } finally {
      setBusy(false);
    }
  }
  const field = 'flex flex-col gap-1.5 text-[14px] font-bold text-ink';
  return (
    <form onSubmit={submit} className="grid grid-cols-1 gap-3 pb-2 sm:grid-cols-2">
      <label className={field}>
        {t('userManagement.email')}
        <input dir="ltr" type="email" className={builderInput} value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <label className={field}>
        {t('userManagement.displayName')}
        <input className={builderInput} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label className={field}>
        {t('userManagement.passwordMinLength')}
        <input dir="ltr" type="password" className={builderInput} value={password} onChange={(e) => setPassword(e.target.value)} />
      </label>
      <label className={field}>
        {t('userManagement.initialRole')}
        <select className={builderInput} value={roleId} onChange={(e) => setRoleId(e.target.value)}>
          <option value="">{t('common.select')}</option>
          {roles.map((r) => (
            <option key={r.id} value={r.id}>
              {roleLabel(r.name)}
            </option>
          ))}
        </select>
      </label>
      <p className="text-[13px] text-ink-soft sm:col-span-2">{t('userManagement.canAddMoreRolesLater')}</p>
      {error && <p className="text-[14px] text-danger sm:col-span-2">{error}</p>}
      <div className="sm:col-span-2">
        <Button type="submit" size="desktop" disabled={busy}>
          {busy ? t('userManagement.creating') : t('userManagement.createUser')}
        </Button>
      </div>
    </form>
  );
}

/** The existing "new role" form: name and an optional description. */
function NewRoleForm({ token, onDone }: { token: string; onDone: () => void }) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();
  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) return setError({ key: 'userManagement.roleNameRequired' });
    setBusy(true);
    try {
      await api.roles.create(token, name, description || undefined);
      onDone();
    } catch (err) {
      setError({ err, fallbackKey: 'userManagement.createRoleError' });
    } finally {
      setBusy(false);
    }
  }
  return (
    <form onSubmit={submit} className="flex flex-col gap-3 pb-2">
      <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
        {t('userManagement.roleName')}
        <input className={builderInput} value={name} onChange={(e) => setName(e.target.value)} placeholder={t('userManagement.roleNamePlaceholder')} />
      </label>
      <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
        {t('userManagement.descriptionOptional')}
        <input className={builderInput} value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>
      {error && <p className="text-[14px] text-danger">{error}</p>}
      <div>
        <Button type="submit" size="desktop" disabled={busy}>
          {busy ? t('userManagement.creating') : t('userManagement.createRole')}
        </Button>
      </div>
    </form>
  );
}
