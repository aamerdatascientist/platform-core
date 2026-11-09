import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { builderInput } from '../components/builder/FieldSettings';
import { DateButton, todayIso } from '../components/DateField';
import { Icon } from '../components/icons';
import { initial } from '../components/shell/ShellData';
import { Button, ErrorState, LoadingState, Segmented, Sheet, TableCard, tdClass, thClass } from '../components/ui';
import { dayLabel, localeFor } from '../forms/recordAnswers';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { useWhatsAppGate } from '../config/whatsapp';
import type { ReportKind, ReportRecipientDto, ReportRunDto, ReportsOverviewDto } from '../types';

const KINDS: ReportKind[] = ['daily', 'weekly', 'monthly'];
type RecipientDraft = Omit<ReportRecipientDto, 'id'>;
const emptyRecipient: RecipientDraft = { name: '', phoneNumber: '', receivesDaily: true, receivesWeekly: true, receivesMonthly: true, isActive: true };
const card = 'flex flex-col rounded-ui-2xl border border-border bg-panel';

/** Saudi "now" as a calendar day and minutes past midnight. */
function saudiNow() {
  const d = new Date(Date.now() + 3 * 3600_000);
  return { day: d.toISOString().slice(0, 10), minutes: d.getUTCHours() * 60 + d.getUTCMinutes() };
}
const addDays = (iso: string, n: number) => new Date(Date.parse(`${iso}T00:00:00Z`) + n * 86_400_000).toISOString().slice(0, 10);
const weekday = (iso: string) => new Date(`${iso}T00:00:00Z`).getUTCDay();
const lastOfMonth = (iso: string) => new Date(Date.UTC(Number(iso.slice(0, 4)), Number(iso.slice(5, 7)), 0)).toISOString().slice(0, 10);

/**
 * The next send day of each report, worked out here from the schedule (24, HANDOFF §9.7):
 * daily = the next working day (Saturday-Thursday), weekly = the next Thursday, monthly = the
 * last day of the month - today counts while the send time hasn't passed.
 */
export function nextSendDay(kind: ReportKind, sendTime: string) {
  const { day, minutes } = saudiNow();
  const [h, m] = sendTime.split(':').map(Number);
  const passed = minutes >= h * 60 + (m || 0);
  const fits = (d: string) => (kind === 'daily' ? weekday(d) !== 5 : kind === 'weekly' ? weekday(d) === 4 : d === lastOfMonth(d));
  let d = passed ? addDays(day, 1) : day;
  for (let i = 0; i < 40 && !fits(d); i++) d = addDays(d, 1);
  return d;
}

/**
 * Stakeholder reports (screen 24): setup pills, the three schedules with their next date, this
 * month's Claude spend against the cap, recipients, "Make a report now" (preview or send, with the
 * existing confirmation) and recent reports - raw technical errors only behind "Show reason".
 */
export function StakeholderReports({ token }: { token: string }) {
  const { t, i18n } = useTranslation();
  const locale = localeFor(i18n.language);
  const [overview, setOverview] = useState<ReportsOverviewDto | null>(null);
  const [error, setError] = useErrorMessage();

  const load = useCallback(async () => {
    try {
      setOverview(await api.reports.overview(token));
      setError(null);
    } catch (err) {
      setError({ err, fallbackKey: 'reports.loadError' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);
  useEffect(() => {
    void load();
  }, [load]);

  if (!overview) return error ? <ErrorState message={error} onRetry={() => (setError(null), void load())} /> : <LoadingState />;
  const [h, m] = overview.sendTime.split(':').map(Number);
  const time = new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' }).format(new Date(Date.UTC(2026, 0, 1, h, m || 0)));
  const share = overview.monthlyBudgetUsd > 0 ? Math.min(1, overview.spentThisMonthUsd / overview.monthlyBudgetUsd) : 0;
  const setup: [string, boolean][] = [
    [t('reports.setup.analysis'), overview.analysisConfigured],
    [t('reports.setup.pdf'), overview.pdfConfigured],
    [t('reports.setup.whatsApp'), overview.whatsAppConfigured],
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-[14px] text-ink-soft">{t('nav.administration')}</span>
          <h1 className="text-[28px] font-extrabold text-ink">{t('reports.title')}</h1>
          <p className="text-[14px] text-ink-soft">{t('reportsV2.intro', { time })}</p>
        </div>
        <div className="flex flex-wrap gap-1.5">
          {setup.map(([name, ok]) => (
            <span key={name} className={`rounded-ui-pill px-3 py-1.5 text-[13px] font-bold ${ok ? 'bg-success-soft text-success' : 'bg-danger-soft text-danger'}`}>
              {ok ? t('reportsV2.setupOk', { name }) : t('reportsV2.setupMissing', { name })}
            </span>
          ))}
        </div>
      </div>
      {error && <p className="text-[14px] text-danger">{error}</p>}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {KINDS.map((kind) => (
          <div key={kind} className={`${card} gap-1 px-4 py-[14px]`}>
            <span className="text-[16px] font-extrabold text-ink">{t(`reports.kind.${kind}`)}</span>
            <span className="text-[13px] text-ink-soft">{t(`reportsV2.${kind}When`, { time })}</span>
            <span className="mt-1.5 text-[13px] font-bold text-accent-strong">{t('reportsV2.next', { date: dayLabel(nextSendDay(kind, overview.sendTime), locale) })}</span>
          </div>
        ))}
        <div className={`${card} gap-2 px-4 py-[14px]`}>
          <span className="text-[13px] text-ink-soft">{t('reports.budget.title')}</span>
          <span dir="ltr" className="text-start text-[22px] font-extrabold text-ink rtl:text-right">
            ${overview.spentThisMonthUsd.toFixed(2)} <span className="text-[14px] font-medium text-ink-soft">/ ${overview.monthlyBudgetUsd.toFixed(2)}</span>
          </span>
          <div aria-hidden="true" className="h-1.5 rounded-ui-pill bg-divider">
            <div className={`h-1.5 min-w-1.5 rounded-ui-pill ${share >= 0.9 ? 'bg-danger' : 'bg-accent'}`} style={{ width: `${Math.round(share * 100)}%` }} />
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-stretch gap-4">
        <Recipients token={token} recipients={overview.recipients} onChanged={load} />
        <RunNow token={token} onDone={load} />
      </div>
      <History token={token} runs={overview.runs} />
    </div>
  );
}

function Recipients({ token, recipients, onChanged }: { token: string; recipients: ReportRecipientDto[]; onChanged: () => Promise<void> }) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  const [draft, setDraft] = useState<RecipientDraft>(emptyRecipient);
  const [menu, setMenu] = useState<string | null>(null);
  const [confirmRemove, setConfirmRemove] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();

  async function save() {
    setBusy(true);
    try {
      await api.reports.saveRecipient(token, draft, editing === 'new' ? undefined : editing ?? undefined);
      setEditing(null);
      await onChanged();
    } catch (err) {
      setError({ err, fallbackKey: 'reports.recipients.saveError' });
    } finally {
      setBusy(false);
    }
  }
  async function remove(id: string) {
    setBusy(true);
    try {
      await api.reports.deleteRecipient(token, id);
      setConfirmRemove(null);
      await onChanged();
    } catch (err) {
      setError({ err, fallbackKey: 'reports.recipients.saveError' });
    } finally {
      setBusy(false);
    }
  }
  const phone = (digits: string) => `+${digits.replace(/^(\d{3})(\d{2})(\d{3})(\d{4})$/, '$1 $2 $3 $4')}`;

  return (
    <section className={`${card} flex-[1_1_360px] gap-3 px-[18px] py-4`}>
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-[17px] font-extrabold text-ink">{t('reportsV2.recipientsTitle', { count: recipients.length })}</h2>
        <Button size="compact" variant="secondary" className="!h-[36px] !min-h-[36px] !border !font-normal" onClick={() => (setDraft(emptyRecipient), setError(null), setEditing('new'))}>
          {t('reports.recipients.add')}
        </Button>
      </div>
      {recipients.length === 0 && <p className="text-[14px] text-ink-soft">{t('reports.recipients.empty')}</p>}
      {recipients.map((r) => (
        <div key={r.id} className={`relative flex items-center gap-3 rounded-ui-lg border border-divider px-[14px] py-3 ${r.isActive ? '' : 'opacity-60'}`}>
          <span aria-hidden="true" className="flex h-[38px] w-[38px] shrink-0 items-center justify-center rounded-full bg-success-soft font-extrabold text-success">
            {initial(r.name)}
          </span>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <span className={`text-[15px] font-bold text-ink ${r.isActive ? '' : 'line-through'}`}>
              <bdi>{r.name}</bdi>
            </span>
            <span dir="ltr" className="text-start text-[13px] text-ink-soft rtl:text-right">
              {phone(r.phoneNumber)}
            </span>
          </div>
          <div className="flex flex-wrap justify-end gap-1">
            {KINDS.filter((k) => (k === 'daily' ? r.receivesDaily : k === 'weekly' ? r.receivesWeekly : r.receivesMonthly)).map((k) => (
              <span key={k} className="rounded-ui-pill bg-accent-soft px-2 py-[3px] text-[12px] font-bold text-accent-strong">
                {t(`reports.kind.${k}Short`)}
              </span>
            ))}
          </div>
          <button type="button" aria-label={t('reports.recipients.edit')} aria-expanded={menu === r.id} onClick={() => setMenu(menu === r.id ? null : r.id)} className="flex h-9 w-9 shrink-0 items-center justify-center rounded-ui-md text-ink-soft hover:bg-field-muted">
            <Icon name="sliders" size={16} />
          </button>
          {menu === r.id && (
            <div className="absolute end-2 top-[52px] z-20 flex min-w-[160px] flex-col rounded-ui-lg border border-border bg-panel p-1 shadow-elev-1">
              <button type="button" className="min-h-[40px] rounded-ui-md px-3 text-start text-[14px] text-ink hover:bg-field-muted" onClick={() => (setMenu(null), setError(null), setDraft({ ...r }), setEditing(r.id))}>
                {t('reports.recipients.edit')}
              </button>
              <button type="button" className="min-h-[40px] rounded-ui-md px-3 text-start text-[14px] text-danger hover:bg-danger-soft" onClick={() => (setMenu(null), setConfirmRemove(r.id))}>
                {t('reports.recipients.delete')}
              </button>
            </div>
          )}
        </div>
      ))}
      <span className="text-[13px] text-ink-soft">{t('reports.recipients.hint')}</span>
      {error && <p className="text-[14px] text-danger">{error}</p>}

      <Sheet open={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? t('reports.recipients.add') : t('reports.recipients.edit')} desktopWidth={520}>
        <div className="flex flex-col gap-3 pb-2">
          <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
            {t('reports.recipients.name')}
            <input className={builderInput} value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
          </label>
          <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
            {t('reports.recipients.phone')}
            <input dir="ltr" inputMode="tel" placeholder="05XXXXXXXX" className={builderInput} value={draft.phoneNumber} onChange={(e) => setDraft({ ...draft, phoneNumber: e.target.value })} />
          </label>
          <div className="flex flex-wrap gap-4 text-[14px] text-ink">
            {(
              [
                ['receivesDaily', 'daily'],
                ['receivesWeekly', 'weekly'],
                ['receivesMonthly', 'monthly'],
              ] as const
            ).map(([field, kind]) => (
              <label key={field} className="flex min-h-[40px] items-center gap-2">
                <input type="checkbox" checked={draft[field]} onChange={(e) => setDraft({ ...draft, [field]: e.target.checked })} />
                {t(`reports.kind.${kind}`)}
              </label>
            ))}
            {editing !== 'new' && (
              <label className="flex min-h-[40px] items-center gap-2">
                <input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />
                {t('reports.recipients.active')}
              </label>
            )}
          </div>
          {error && <p className="text-[14px] text-danger">{error}</p>}
          <div>
            <Button size="desktop" disabled={busy || !draft.name.trim() || !draft.phoneNumber.trim()} onClick={save}>
              {t('reports.recipients.save')}
            </Button>
          </div>
        </div>
      </Sheet>
      <Sheet open={confirmRemove !== null} onClose={() => setConfirmRemove(null)} title={t('reports.recipients.delete')} desktopWidth={440}>
        <div className="flex flex-col gap-3 pb-2">
          <p className="text-[14px] text-ink">{t('reports.recipients.confirmDelete', { name: recipients.find((r) => r.id === confirmRemove)?.name ?? '' })}</p>
          <div className="flex gap-2">
            <Button size="desktop" variant="danger" disabled={busy} onClick={() => confirmRemove && remove(confirmRemove)}>
              {t('reports.recipients.delete')}
            </Button>
            <Button size="desktop" variant="link" onClick={() => setConfirmRemove(null)}>
              {t('reports.cancel')}
            </Button>
          </div>
        </div>
      </Sheet>
    </section>
  );
}

function RunNow({ token, onDone }: { token: string; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const [kind, setKind] = useState<ReportKind>('daily');
  const [date, setDate] = useState(todayIso());
  const [withAnalysis, setWithAnalysis] = useState(true);
  const [busy, setBusy] = useState<'preview' | 'send' | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [result, setResult] = useState<ReportRunDto | null>(null);
  // "Send now" delivers on WhatsApp - until it's connected it only says so (preview still works).
  const whatsapp = useWhatsAppGate();
  const [error, setError] = useErrorMessage();

  async function run(deliver: boolean) {
    setConfirming(false);
    setBusy(deliver ? 'send' : 'preview');
    setResult(null);
    setError(null);
    try {
      setResult(await api.reports.run(token, { kind, to: date || null, withAnalysis, deliver }));
      await onDone();
    } catch (err) {
      setError({ err, fallbackKey: 'reports.run.error' });
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className={`${card} flex-[1_1_360px] gap-3 px-[18px] py-4`}>
      <h2 className="text-[17px] font-extrabold text-ink">{t('reports.run.title')}</h2>
      <Segmented<ReportKind> size="desktop" label={t('reports.run.kind')} value={kind} onChange={setKind} options={KINDS.map((k) => ({ value: k, label: t(`reports.kind.${k}`) }))} />
      <div className="flex flex-wrap items-center gap-[10px] text-[14px]">
        <span className="text-ink-soft">{t('reports.run.date')}</span>
        <DateButton value={date} onPick={setDate} title={t('reports.run.date')} maxDate={todayIso()} className="h-[38px] px-3" />
        <label className="ms-auto flex min-h-[40px] items-center gap-2 text-ink">
          <input type="checkbox" className="h-[18px] w-[18px] accent-[var(--accent)]" checked={withAnalysis} onChange={(e) => setWithAnalysis(e.target.checked)} />
          {t('reports.run.withAnalysis')}
        </label>
      </div>
      {error && <p className="text-[14px] text-danger">{error}</p>}
      {whatsapp.notice && (
        <p role="status" className="rounded-ui-lg bg-warning-soft px-3 py-2 text-[14px] text-warning-strong">
          {t('whatsapp.notReady')}
        </p>
      )}
      {result && <RunResult token={token} run={result} />}
      <div className="mt-auto flex gap-2">
        <Button size="desktop" variant="secondary" block className="!h-[44px] !font-normal" disabled={busy !== null} onClick={() => run(false)}>
          {busy === 'preview' ? t('reports.run.working') : t('reportsV2.previewNoSend')}
        </Button>
        <Button size="desktop" block className="!h-[44px]" disabled={busy !== null} onClick={() => whatsapp.guard(() => setConfirming(true))}>
          {busy === 'send' ? t('reports.run.working') : t('reports.run.send')}
        </Button>
      </div>
      <Sheet open={confirming} onClose={() => setConfirming(false)} title={t('reports.run.send')} desktopWidth={460}>
        <div className="flex flex-col gap-3 pb-2">
          <p className="text-[14px] text-ink">{t('reports.run.confirmSend', { kind: t(`reports.kind.${kind}`) })}</p>
          <div className="flex gap-2">
            <Button size="desktop" onClick={() => run(true)}>
              {t('reports.run.send')}
            </Button>
            <Button size="desktop" variant="link" onClick={() => setConfirming(false)}>
              {t('reports.cancel')}
            </Button>
          </div>
        </div>
      </Sheet>
    </section>
  );
}

/** A run's outcome in plain words (no raw errors - those are behind "Show reason"). */
function outcome(run: ReportRunDto): { tone: 'success' | 'warning' | 'danger'; key: string } {
  if (run.status === 'failed') return { tone: 'danger', key: 'reportsV2.resultFailed' };
  if (run.deliveries.some((d) => d.status === 'failed')) return { tone: 'warning', key: 'reportsV2.resultNotSent' };
  if (run.analysis !== 'written') return { tone: 'warning', key: 'reportsV2.resultDoneNoAnalysis' };
  return { tone: 'success', key: 'reportsV2.resultDoneAnalysis' };
}
const DOT = { success: 'bg-success text-success', warning: 'bg-warning text-warning', danger: 'bg-danger text-danger' };

function RunResult({ token, run }: { token: string; run: ReportRunDto }) {
  return (
    <div className="flex flex-col gap-2 rounded-ui-lg bg-field-muted px-3 py-2.5 text-[14px]" role="status">
      <Outcome run={run} />
      {run.hasPdf && <Downloads token={token} runId={run.id} />}
    </div>
  );
}

function Outcome({ run }: { run: ReportRunDto }) {
  const { t } = useTranslation();
  const [reason, setReason] = useState(false);
  const [copied, setCopied] = useState(false);
  const o = outcome(run);
  const details = [run.error, ...run.deliveries.filter((d) => d.error).map((d) => `${d.recipientName}: ${d.error}`)].filter(Boolean).join('\n');
  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <span className={`inline-flex items-center gap-1.5 font-bold ${DOT[o.tone].split(' ')[1]}`}>
        <span aria-hidden="true" className={`h-2 w-2 rounded-full ${DOT[o.tone].split(' ')[0]}`} />
        {t(o.key)}
      </span>
      {details && (
        <button type="button" onClick={() => setReason(true)} className="h-[28px] rounded-[8px] border border-border bg-panel px-2 text-[12px] text-ink">
          {t('reportsV2.showReason')}
        </button>
      )}
      <Sheet open={reason} onClose={() => setReason(false)} title={t('reportsV2.reasonTitle')} desktopWidth={560}>
        <div className="flex flex-col gap-3 pb-2">
          <pre dir="ltr" className="max-h-[50vh] overflow-auto whitespace-pre-wrap rounded-ui-md bg-field-muted p-3 text-start font-mono text-[12px] text-ink">
            {details}
          </pre>
          <Button
            size="desktop"
            variant="secondary"
            className="self-start"
            onClick={() => {
              void navigator.clipboard?.writeText(details).then(() => setCopied(true));
            }}
          >
            {copied ? t('reportsV2.copied') : t('reportsV2.copy')}
          </Button>
        </div>
      </Sheet>
    </span>
  );
}

/** Phone and A4 downloads. The tab is opened inside the click (pop-up blockers), then pointed at the link. */
function Downloads({ token, runId }: { token: string; runId: string }) {
  const { t } = useTranslation();
  async function open(phone: boolean) {
    const tab = window.open('', '_blank');
    try {
      const { url } = await api.reports.download(token, runId, phone);
      if (tab) tab.location.href = url;
      else window.location.href = url;
    } catch {
      tab?.close();
    }
  }
  const b = 'h-[32px] whitespace-nowrap rounded-[8px] border border-border bg-panel px-[10px] text-[13px] text-ink hover:border-ink-soft';
  return (
    <span className="inline-flex gap-1.5">
      <button type="button" onClick={() => open(true)} className={b}>
        {t('reports.download.phone')}
      </button>
      <button type="button" onClick={() => open(false)} className={b}>
        {t('reports.download.a4')}
      </button>
    </span>
  );
}

function History({ token, runs }: { token: string; runs: ReportRunDto[] }) {
  const { t, i18n } = useTranslation();
  const locale = localeFor(i18n.language);
  const when = (iso: string) => {
    const day = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', timeZone: 'Asia/Riyadh' }).format(new Date(iso));
    const time = new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', timeZone: 'Asia/Riyadh' }).format(new Date(iso));
    return `${day} · ${time}`;
  };
  return (
    <TableCard className="[&_table]:min-w-[680px]">
      <caption className="px-[18px] py-[14px] text-start text-[17px] font-extrabold text-ink">{t('reports.history.title')}</caption>
      <thead>
        <tr>
          <th className={thClass}>{t('reports.history.when')}</th>
          <th className={thClass}>{t('reports.history.report')}</th>
          <th className={thClass}>{t('reports.history.result')}</th>
          <th className={thClass}>{t('reports.history.files')}</th>
        </tr>
      </thead>
      <tbody>
        {runs.length === 0 && (
          <tr>
            <td className={`${tdClass} text-ink-soft`} colSpan={4}>
              {t('reports.history.empty')}
            </td>
          </tr>
        )}
        {runs.map((run) => (
          <tr key={run.id}>
            <td className={`${tdClass} whitespace-nowrap`}>{when(run.createdAtUtc)}</td>
            <td className={tdClass}>
              <span className="font-bold">{t(`reports.kind.${run.kind}`)}</span> <span className="text-ink-soft">· {t(`reports.trigger.${run.trigger}`, run.trigger)}</span>
            </td>
            <td className={tdClass}>
              <Outcome run={run} />
            </td>
            <td className={tdClass}>{run.hasPdf && <Downloads token={token} runId={run.id} />}</td>
          </tr>
        ))}
      </tbody>
    </TableCard>
  );
}
