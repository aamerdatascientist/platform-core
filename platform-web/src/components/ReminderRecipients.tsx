import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { builderInput } from './builder/FieldSettings';
import { Icon } from './icons';
import { initial, useShell } from './shell/ShellData';
import { Button, ErrorState, Sheet } from './ui';
import { saudiTodayIso } from '../forms/fieldValues';
import { useLocalized } from '../forms/localized';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { ContactLink, userContactUrl } from './ContactLink';
import type { FormAssignmentDto, UserSummaryDto } from '../types';

const phone = (digits: string) => `+${digits.replace(/^(\d{3})(\d{2})(\d{3})(\d{4})$/, '$1 $2 $3 $4')}`;

interface Recipient {
  user: UserSummaryDto;
  assignments: FormAssignmentDto[];
}

/**
 * Who gets the form reminders and the new-assignment message (owner, 2026-11-13: "add
 * recipients in both, and the ability to add more"). A recipient is a user with an active form
 * assignment; their WhatsApp number is the user's phone number. "Add a recipient" assigns a form
 * to a user (and saves their number) - the same as the Form assignments page, from here.
 * `compact` shows names only (the new-assignment card), the full list is on the reminders card.
 */
export function ReminderRecipients({
  token,
  compact = false,
  version,
  onChanged,
  onNavigate,
}: {
  token: string;
  compact?: boolean;
  version: number;
  onChanged: () => void;
  /** Leaving the page from a warning link goes through this (the page asks first when it has unsaved changes). */
  onNavigate?: (to: string) => void;
}) {
  const { t } = useTranslation();
  const { forms } = useShell();
  const { formName } = useLocalized();
  const [users, setUsers] = useState<UserSummaryDto[] | null>(null);
  const [assignments, setAssignments] = useState<FormAssignmentDto[]>([]);
  const [failed, setFailed] = useState(false);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<UserSummaryDto | null>(null);
  const [stopping, setStopping] = useState<Recipient | null>(null);
  const [menu, setMenu] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [u, a] = await Promise.all([api.users.list(token), api.assignments.list(token)]);
      setUsers(u);
      setAssignments(a);
      setFailed(false);
    } catch {
      setFailed(true);
    }
  }, [token]);
  useEffect(() => {
    void load();
  }, [load, version]);

  const recipients = useMemo<Recipient[]>(() => {
    const byUser = new Map<string, FormAssignmentDto[]>();
    for (const a of assignments.filter((x) => x.isActive)) byUser.set(a.userId, [...(byUser.get(a.userId) ?? []), a]);
    return [...byUser.entries()]
      .map(([id, list]) => ({ user: users?.find((u) => u.id === id), assignments: list }))
      .filter((r): r is Recipient => !!r.user)
      .sort((a, b) => a.user.displayName.localeCompare(b.user.displayName));
  }, [assignments, users]);

  const nameOf = (a: FormAssignmentDto) => {
    const f = forms?.find((x) => x.id === a.formId);
    return f ? formName(f) : a.formName;
  };
  const changed = () => {
    void load();
    onChanged();
  };

  if (failed) return <ErrorState message={t('messages.people.loadError')} onRetry={() => void load()} />;
  if (!users) return null;

  const addButton = (
    <Button size="compact" variant="secondary" className="!h-[36px] !min-h-[36px] !border !font-normal" onClick={() => setAdding(true)}>
      {t('messages.people.add')}
    </Button>
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-[15px] font-extrabold text-ink">{t('messages.people.title', { count: recipients.length })}</h3>
        {addButton}
      </div>

      {recipients.length === 0 && <p className="text-[14px] text-ink-soft">{t('messages.people.empty')}</p>}

      {compact
        ? recipients.length > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {recipients.map((r) =>
                r.user.phoneNumber || r.user.telegramLinked ? (
                  <span key={r.user.id} className="rounded-ui-pill bg-accent-soft px-3 py-1.5 text-[13px] font-bold text-accent-strong">
                    <bdi>{r.user.displayName}</bdi>
                  </span>
                ) : (
                  // Admin flows F3-1: the warning-coloured chip (no way to reach them) opens their account.
                  <ContactLink
                    key={r.user.id}
                    to={userContactUrl(r.user.id, 'whatsapp', 'messages')}
                    label={t('flows.addWhatsappFor', { name: r.user.displayName })}
                    onNavigate={onNavigate}
                    className="rounded-ui-pill bg-warning-soft px-3 py-1.5 text-[13px] font-bold text-warning-strong"
                  >
                    <bdi>{r.user.displayName}</bdi>
                  </ContactLink>
                ),
              )}
            </div>
          )
        : recipients.map((r) => (
            <div key={r.user.id} className="relative flex items-center gap-3 rounded-ui-lg border border-divider px-[14px] py-3">
              <span aria-hidden="true" className="flex h-[38px] w-[38px] shrink-0 items-center justify-center rounded-full bg-success-soft font-extrabold text-success">
                {initial(r.user.displayName)}
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <span className="break-words text-[15px] font-bold text-ink">
                  <bdi>{r.user.displayName}</bdi>
                </span>
                {r.user.phoneNumber ? (
                  <span dir="ltr" className="text-start text-[13px] text-ink-soft rtl:text-right">
                    {phone(r.user.phoneNumber)}
                  </span>
                ) : (
                  <ContactLink
                    to={userContactUrl(r.user.id, 'whatsapp', 'messages')}
                    label={t('flows.addWhatsappFor', { name: r.user.displayName })}
                    onNavigate={onNavigate}
                    className="self-start text-[13px] font-bold text-warning-strong"
                  >
                    {t('messages.people.noNumber')}
                  </ContactLink>
                )}
              </div>
              <div className="hidden flex-wrap justify-end gap-1 sm:flex">
                {r.assignments.map((a) => (
                  <span key={a.id} className="rounded-ui-pill bg-accent-soft px-2 py-[3px] text-[12px] font-bold text-accent-strong">
                    <bdi>{nameOf(a)}</bdi>
                  </span>
                ))}
              </div>
              <button
                type="button"
                aria-label={t('messages.people.options', { name: r.user.displayName })}
                aria-expanded={menu === r.user.id}
                onClick={() => setMenu(menu === r.user.id ? null : r.user.id)}
                className="flex h-9 w-9 shrink-0 items-center justify-center rounded-ui-md text-ink-soft hover:bg-field-muted"
              >
                <Icon name="sliders" size={16} />
              </button>
              {menu === r.user.id && (
                <div className="absolute end-2 top-[52px] z-20 flex min-w-[200px] flex-col rounded-ui-lg border border-border bg-panel p-1 shadow-elev-1">
                  <button type="button" className="min-h-[40px] rounded-ui-md px-3 text-start text-[14px] text-ink hover:bg-field-muted" onClick={() => (setMenu(null), setEditing(r.user))}>
                    {t('messages.people.editNumber')}
                  </button>
                  <button type="button" className="min-h-[40px] rounded-ui-md px-3 text-start text-[14px] text-danger hover:bg-danger-soft" onClick={() => (setMenu(null), setStopping(r))}>
                    {t('messages.people.stop')}
                  </button>
                </div>
              )}
            </div>
          ))}
      {!compact && <span className="text-[13px] text-ink-soft">{t('messages.people.hint')}</span>}

      <AddRecipient token={token} open={adding} users={users} onClose={() => setAdding(false)} onDone={() => (setAdding(false), changed())} />
      <EditNumber token={token} user={editing} onClose={() => setEditing(null)} onDone={() => (setEditing(null), changed())} />
      <StopRecipient token={token} recipient={stopping} onClose={() => setStopping(null)} onDone={() => (setStopping(null), changed())} />
    </div>
  );
}

/** Pick a person and a form, confirm their WhatsApp number: assigns the form from today. */
function AddRecipient({ token, open, users, onClose, onDone }: { token: string; open: boolean; users: UserSummaryDto[]; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation();
  const { forms } = useShell();
  const { formName } = useLocalized();
  const published = (forms ?? []).filter((f) => f.status === 'Published');
  const active = users.filter((u) => u.isActive);
  const [userId, setUserId] = useState('');
  const [formId, setFormId] = useState('');
  const [number, setNumber] = useState('');
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useErrorMessage();

  useEffect(() => {
    if (!open) return;
    setUserId('');
    setFormId(published.length === 1 ? published[0].id : '');
    setNumber('');
    setNote(null);
    setError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const pickUser = (id: string) => {
    setUserId(id);
    setNumber(active.find((u) => u.id === id)?.phoneNumber ?? '');
  };

  async function save() {
    const user = active.find((u) => u.id === userId);
    if (!user || !formId) return;
    setBusy(true);
    setError(null);
    try {
      const typed = number.trim();
      if (typed && typed.replace(/\D/g, '') !== (user.phoneNumber ?? '')) await api.users.setPhoneNumber(token, user.id, typed);
      const result = await api.assignments.create(token, { formDefinitionId: formId, userIds: [user.id], startDate: saudiTodayIso(), endDate: null, note: null });
      if (result.created === 0) {
        setNote(t('messages.people.already'));
        return;
      }
      onDone();
    } catch (err) {
      setError({ err, fallbackKey: 'messages.people.saveError' });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Sheet open={open} onClose={onClose} title={t('messages.people.add')} desktopWidth={520}>
      <div className="flex flex-col gap-3 pb-2">
        <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
          {t('messages.people.person')}
          <select className={builderInput} value={userId} onChange={(e) => pickUser(e.target.value)}>
            <option value="">{t('messages.people.pickPerson')}</option>
            {active.map((u) => (
              <option key={u.id} value={u.id}>
                {u.displayName}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
          {t('messages.people.form')}
          <select className={builderInput} value={formId} onChange={(e) => setFormId(e.target.value)}>
            <option value="">{t('messages.people.pickForm')}</option>
            {published.map((f) => (
              <option key={f.id} value={f.id}>
                {formName(f)}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
          {t('messages.people.number')}
          <input dir="ltr" inputMode="tel" placeholder="05XXXXXXXX" className={builderInput} value={number} onChange={(e) => setNumber(e.target.value)} />
          <span className="text-[13px] font-normal text-ink-soft">{t('messages.people.numberHint')}</span>
        </label>
        {note && <p className="text-[14px] text-warning-strong">{note}</p>}
        {error && <p className="text-[14px] text-danger">{error}</p>}
        <div>
          <Button size="desktop" disabled={busy || !userId || !formId} onClick={() => void save()}>
            {busy ? t('messages.saving') : t('messages.people.addButton')}
          </Button>
        </div>
      </div>
    </Sheet>
  );
}

function EditNumber({ token, user, onClose, onDone }: { token: string; user: UserSummaryDto | null; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation();
  const [number, setNumber] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();
  useEffect(() => {
    setNumber(user?.phoneNumber ? `+${user.phoneNumber}` : '');
    setError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user]);

  async function save() {
    if (!user) return;
    setBusy(true);
    try {
      await api.users.setPhoneNumber(token, user.id, number.trim() || null);
      onDone();
    } catch (err) {
      setError({ err, fallbackKey: 'messages.people.saveError' });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Sheet open={user !== null} onClose={onClose} title={t('messages.people.editNumber')} desktopWidth={440}>
      <div className="flex flex-col gap-3 pb-2">
        <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
          <bdi>{user?.displayName}</bdi>
          <input dir="ltr" inputMode="tel" placeholder="05XXXXXXXX" className={builderInput} value={number} onChange={(e) => setNumber(e.target.value)} />
          <span className="text-[13px] font-normal text-ink-soft">{t('messages.people.numberClearHint')}</span>
        </label>
        {error && <p className="text-[14px] text-danger">{error}</p>}
        <div>
          <Button size="desktop" disabled={busy} onClick={() => void save()}>
            {busy ? t('messages.saving') : t('messages.people.saveNumber')}
          </Button>
        </div>
      </div>
    </Sheet>
  );
}

/** Ends every active assignment of that person today, so they stop getting reminders. */
function StopRecipient({ token, recipient, onClose, onDone }: { token: string; recipient: Recipient | null; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();
  async function stop() {
    if (!recipient) return;
    setBusy(true);
    try {
      for (const a of recipient.assignments) await api.assignments.stop(token, a.id);
      onDone();
    } catch (err) {
      setError({ err, fallbackKey: 'messages.people.saveError' });
    } finally {
      setBusy(false);
    }
  }
  return (
    <Sheet open={recipient !== null} onClose={onClose} title={t('messages.people.stop')} desktopWidth={440}>
      <div className="flex flex-col gap-3 pb-2">
        <p className="text-[14px] text-ink">{t('messages.people.confirmStop', { name: recipient?.user.displayName ?? '' })}</p>
        {error && <p className="text-[14px] text-danger">{error}</p>}
        <div className="flex gap-2">
          <Button size="desktop" variant="danger" disabled={busy} onClick={() => void stop()}>
            {t('messages.people.stop')}
          </Button>
          <Button size="desktop" variant="link" onClick={onClose}>
            {t('reports.cancel')}
          </Button>
        </div>
      </div>
    </Sheet>
  );
}
