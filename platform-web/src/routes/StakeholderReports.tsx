import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { DateField, todayIso } from '../components/DateField';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { ReportKind, ReportRecipientDto, ReportRunDto, ReportsOverviewDto } from '../types';

interface StakeholderReportsProps {
  token: string;
}

const sectionClass = 'rounded border border-border bg-panel p-4 shadow-recessed sm:p-5';
const headingClass = 'mb-3 text-label font-medium uppercase tracking-wider text-ink-soft';
const inputClass = 'w-full rounded border border-border bg-bg px-2 py-1.5 text-sm text-ink focus:border-accent focus:outline-none';
const buttonClass = 'rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:border-ink disabled:opacity-50';
const primaryClass = 'rounded bg-accent px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50';
const thClass = 'whitespace-nowrap border-b border-border px-2 py-2 text-start text-label font-medium uppercase tracking-wider text-ink-soft';
const tdClass = 'border-b border-border px-2 py-2 align-top';
const KINDS: ReportKind[] = ['daily', 'weekly', 'monthly'];

type RecipientDraft = Omit<ReportRecipientDto, 'id'>;
const emptyRecipient: RecipientDraft = {
  name: '',
  phoneNumber: '',
  receivesDaily: true,
  receivesWeekly: true,
  receivesMonthly: true,
  isActive: true,
};

/**
 * Administrators: the stakeholder reports - daily, weekly and monthly PDFs with Claude's
 * written analysis, sent on WhatsApp at 6 pm. Who receives them, this month's Claude spend
 * against the cap, a preview or an immediate send, and the history with downloads.
 */
export function StakeholderReports({ token }: StakeholderReportsProps) {
  const { t } = useTranslation();
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

  return (
    <div className="max-w-4xl space-y-4">
      <div>
        <Link to="/settings" className="text-xs text-ink-soft hover:text-ink">
          {t('reports.back')}
        </Link>
        <h2 className="mt-1 font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{t('reports.title')}</h2>
        <p className="mt-1 text-sm text-ink-soft">{t('reports.intro', { time: overview?.sendTime ?? '18:00' })}</p>
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}
      {!overview ? (
        !error && <LoadingSpinner />
      ) : (
        <>
          <SetupAndBudget overview={overview} />
          <Recipients token={token} recipients={overview.recipients} onChanged={load} />
          <RunNow token={token} onDone={load} />
          <History token={token} runs={overview.runs} />
        </>
      )}
    </div>
  );
}

function SetupAndBudget({ overview }: { overview: ReportsOverviewDto }) {
  const { t } = useTranslation();
  const share = overview.monthlyBudgetUsd > 0 ? Math.min(1, overview.spentThisMonthUsd / overview.monthlyBudgetUsd) : 0;
  const checks: [string, boolean][] = [
    ['reports.setup.analysis', overview.analysisConfigured],
    ['reports.setup.pdf', overview.pdfConfigured],
    ['reports.setup.whatsApp', overview.whatsAppConfigured],
  ];
  return (
    <section className={sectionClass}>
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <h3 className={headingClass}>{t('reports.setup.title')}</h3>
          <ul className="space-y-1 text-sm">
            {checks.map(([key, ok]) => (
              <li key={key} className={ok ? 'text-success' : 'text-danger'}>
                {ok ? '✓' : '✗'} {t(key)}
              </li>
            ))}
          </ul>
          <p className="mt-2 text-xs text-ink-soft" dir="ltr">
            {overview.model}
          </p>
        </div>
        <div>
          <h3 className={headingClass}>{t('reports.budget.title')}</h3>
          <p className="text-2xl font-semibold text-ink" dir="ltr">
            ${overview.spentThisMonthUsd.toFixed(2)}{' '}
            <span className="text-sm font-normal text-ink-soft">/ ${overview.monthlyBudgetUsd.toFixed(2)}</span>
          </p>
          <div className="mt-2 h-2 overflow-hidden rounded-full bg-bg">
            <div
              className={`h-full rounded-full ${share >= 0.9 ? 'bg-danger' : 'bg-accent'}`}
              style={{ width: `${Math.round(share * 100)}%` }}
            />
          </div>
          <p className="mt-2 text-xs text-ink-soft">{t('reports.budget.hint')}</p>
        </div>
      </div>
    </section>
  );
}

function Recipients({
  token,
  recipients,
  onChanged,
}: {
  token: string;
  recipients: ReportRecipientDto[];
  onChanged: () => Promise<void>;
}) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  const [draft, setDraft] = useState<RecipientDraft>(emptyRecipient);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();

  function start(recipient?: ReportRecipientDto) {
    setError(null);
    setEditing(recipient?.id ?? 'new');
    setDraft(recipient ? { ...recipient } : emptyRecipient);
  }

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

  async function remove(recipient: ReportRecipientDto) {
    if (!window.confirm(t('reports.recipients.confirmDelete', { name: recipient.name }))) return;
    try {
      await api.reports.deleteRecipient(token, recipient.id);
      await onChanged();
    } catch (err) {
      setError({ err, fallbackKey: 'reports.recipients.saveError' });
    }
  }

  const kindsOf = (r: RecipientDraft) =>
    KINDS.filter((k) => (k === 'daily' ? r.receivesDaily : k === 'weekly' ? r.receivesWeekly : r.receivesMonthly))
      .map((k) => t(`reports.kind.${k}`))
      .join(' · ');

  const form = (
    <div className="space-y-3 rounded border border-border bg-bg p-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <div>
          <label htmlFor="recipient-name" className="mb-1 block text-xs text-ink-soft">
            {t('reports.recipients.name')}
          </label>
          <input
            id="recipient-name"
            className={inputClass}
            value={draft.name}
            onChange={(e) => setDraft({ ...draft, name: e.target.value })}
          />
        </div>
        <div>
          <label htmlFor="recipient-phone" className="mb-1 block text-xs text-ink-soft">
            {t('reports.recipients.phone')}
          </label>
          <input
            id="recipient-phone"
            dir="ltr"
            inputMode="tel"
            className={inputClass}
            placeholder="05XXXXXXXX"
            value={draft.phoneNumber}
            onChange={(e) => setDraft({ ...draft, phoneNumber: e.target.value })}
          />
        </div>
      </div>
      <div className="flex flex-wrap gap-4 text-sm text-ink">
        {(
          [
            ['receivesDaily', 'daily'],
            ['receivesWeekly', 'weekly'],
            ['receivesMonthly', 'monthly'],
          ] as const
        ).map(([field, kind]) => (
          <label key={field} className="flex items-center gap-2">
            <input
              type="checkbox"
              checked={draft[field]}
              onChange={(e) => setDraft({ ...draft, [field]: e.target.checked })}
            />
            {t(`reports.kind.${kind}`)}
          </label>
        ))}
        {editing !== 'new' && (
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />
            {t('reports.recipients.active')}
          </label>
        )}
      </div>
      {error && <p className="text-sm text-danger">{error}</p>}
      <div className="flex gap-2">
        <button type="button" onClick={save} disabled={busy || !draft.name.trim() || !draft.phoneNumber.trim()} className={primaryClass}>
          {t('reports.recipients.save')}
        </button>
        <button type="button" onClick={() => setEditing(null)} className="px-3 py-1.5 text-sm text-ink-soft">
          {t('reports.cancel')}
        </button>
      </div>
    </div>
  );

  return (
    <section className={sectionClass}>
      <h3 className={headingClass}>{t('reports.recipients.title')}</h3>
      <p className="mb-3 text-xs text-ink-soft">{t('reports.recipients.hint')}</p>
      <ul className="space-y-2">
        {recipients.map((r) =>
          editing === r.id ? (
            <li key={r.id}>{form}</li>
          ) : (
            <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 rounded border border-border p-3">
              <div>
                <p className={`text-sm font-medium ${r.isActive ? 'text-ink' : 'text-ink-soft line-through'}`}>{r.name}</p>
                <p className="text-xs text-ink-soft">
                  <span dir="ltr">+{r.phoneNumber}</span> · {kindsOf(r) || t('reports.recipients.none')}
                </p>
              </div>
              <div className="flex gap-2">
                <button type="button" onClick={() => start(r)} className={buttonClass}>
                  {t('reports.recipients.edit')}
                </button>
                <button type="button" onClick={() => remove(r)} className="px-2 py-1.5 text-sm text-danger">
                  {t('reports.recipients.delete')}
                </button>
              </div>
            </li>
          ),
        )}
        {recipients.length === 0 && editing !== 'new' && <li className="text-sm text-ink-soft">{t('reports.recipients.empty')}</li>}
      </ul>
      <div className="mt-3">
        {editing === 'new' ? (
          form
        ) : (
          <button type="button" onClick={() => start()} className={buttonClass}>
            {t('reports.recipients.add')}
          </button>
        )}
      </div>
    </section>
  );
}

function RunNow({ token, onDone }: { token: string; onDone: () => Promise<void> }) {
  const { t } = useTranslation();
  const [kind, setKind] = useState<ReportKind>('daily');
  const [date, setDate] = useState(todayIso());
  const [withAnalysis, setWithAnalysis] = useState(true);
  const [busy, setBusy] = useState<'preview' | 'send' | null>(null);
  const [result, setResult] = useState<ReportRunDto | null>(null);
  const [error, setError] = useErrorMessage();

  async function run(deliver: boolean) {
    if (deliver && !window.confirm(t('reports.run.confirmSend', { kind: t(`reports.kind.${kind}`) }))) return;
    setBusy(deliver ? 'send' : 'preview');
    setResult(null);
    setError(null);
    try {
      const run = await api.reports.run(token, { kind, to: date || null, withAnalysis, deliver });
      setResult(run);
      await onDone();
    } catch (err) {
      setError({ err, fallbackKey: 'reports.run.error' });
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className={sectionClass}>
      <h3 className={headingClass}>{t('reports.run.title')}</h3>
      <div className="grid gap-3 sm:grid-cols-3">
        <div>
          <label htmlFor="run-kind" className="mb-1 block text-xs text-ink-soft">
            {t('reports.run.kind')}
          </label>
          <select id="run-kind" className={inputClass} value={kind} onChange={(e) => setKind(e.target.value as ReportKind)}>
            {KINDS.map((k) => (
              <option key={k} value={k}>
                {t(`reports.kind.${k}`)}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="run-date" className="mb-1 block text-xs text-ink-soft">
            {t('reports.run.date')}
          </label>
          <DateField id="run-date" value={date} onChange={setDate} required className={inputClass} maxDate={todayIso()} />
        </div>
        <label className="flex items-end gap-2 pb-1.5 text-sm text-ink">
          <input type="checkbox" checked={withAnalysis} onChange={(e) => setWithAnalysis(e.target.checked)} />
          {t('reports.run.withAnalysis')}
        </label>
      </div>
      <p className="mt-2 text-xs text-ink-soft">{t('reports.run.hint')}</p>
      <div className="mt-3 flex flex-wrap gap-2">
        <button type="button" onClick={() => run(false)} disabled={busy !== null} className={buttonClass}>
          {busy === 'preview' ? t('reports.run.working') : t('reports.run.preview')}
        </button>
        <button type="button" onClick={() => run(true)} disabled={busy !== null} className={primaryClass}>
          {busy === 'send' ? t('reports.run.working') : t('reports.run.send')}
        </button>
      </div>
      {error && <p className="mt-2 text-sm text-danger">{error}</p>}
      {result && (
        <div className="mt-3 space-y-2 rounded border border-border bg-bg p-3 text-sm">
          <RunStatus run={result} />
          {result.hasPdf && <Downloads token={token} runId={result.id} />}
        </div>
      )}
    </section>
  );
}

function RunStatus({ run }: { run: ReportRunDto }) {
  const { t } = useTranslation();
  return (
    <div className="space-y-1">
      <p className={run.status === 'failed' ? 'text-danger' : 'text-ink'}>
        {t(`reports.status.${run.status}`, run.status)}
        {run.analysis && ` · ${t(`reports.analysis.${run.analysis}`, run.analysis)}`}
        {run.costUsd > 0 && <span dir="ltr"> · ${run.costUsd.toFixed(3)}</span>}
      </p>
      {run.error && (
        <p className="text-xs text-danger" dir="auto">
          {run.error}
        </p>
      )}
      {run.deliveries.map((d) => (
        <p key={d.phoneNumber} className={`text-xs ${d.status === 'failed' ? 'text-danger' : 'text-ink-soft'}`} dir="auto">
          {d.recipientName}: {t(`reports.delivery.${d.status}`, d.status)}
          {d.error ? ` - ${d.error}` : ''}
        </p>
      ))}
    </div>
  );
}

/** A4 and phone downloads. The tab is opened inside the click (pop-up blockers), then pointed at the link. */
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
  return (
    <div className="flex flex-wrap gap-2">
      <button type="button" onClick={() => open(true)} className={buttonClass}>
        {t('reports.download.phone')}
      </button>
      <button type="button" onClick={() => open(false)} className={buttonClass}>
        {t('reports.download.a4')}
      </button>
    </div>
  );
}

function History({ token, runs }: { token: string; runs: ReportRunDto[] }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  const when = (iso: string) =>
    new Date(iso).toLocaleString(locale, { timeZone: 'Asia/Riyadh', dateStyle: 'medium', timeStyle: 'short' });
  return (
    <section className={sectionClass}>
      <h3 className={headingClass}>{t('reports.history.title')}</h3>
      {runs.length === 0 ? (
        <p className="text-sm text-ink-soft">{t('reports.history.empty')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr>
                <th className={thClass}>{t('reports.history.when')}</th>
                <th className={thClass}>{t('reports.history.report')}</th>
                <th className={thClass}>{t('reports.history.result')}</th>
                <th className={thClass}>{t('reports.history.files')}</th>
              </tr>
            </thead>
            <tbody>
              {runs.map((run) => (
                <tr key={run.id}>
                  <td className={`${tdClass} whitespace-nowrap text-ink-soft`}>{when(run.createdAtUtc)}</td>
                  <td className={tdClass}>
                    <span className="text-ink">{t(`reports.kind.${run.kind}`)}</span>
                    <span className="block text-xs text-ink-soft">
                      {run.periodStart === run.periodEnd ? run.periodEnd : `${run.periodStart} – ${run.periodEnd}`} ·{' '}
                      {t(`reports.trigger.${run.trigger}`, run.trigger)}
                    </span>
                  </td>
                  <td className={tdClass}>
                    <RunStatus run={run} />
                  </td>
                  <td className={tdClass}>{run.hasPdf && <Downloads token={token} runId={run.id} />}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
