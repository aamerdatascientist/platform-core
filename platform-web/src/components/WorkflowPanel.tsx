import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api/client';
import { StatusLed } from './StatusLed';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { WorkflowStatusDto } from '../types';

interface WorkflowPanelProps {
  token: string;
  recordId: string;
  onChanged?: () => void;
}

/**
 * A record with no workflow attached to its form isn't an error - GetWorkflowStatusQuery
 * 404s in that case, and this treats that specifically as "render nothing", not a failure
 * state. Only a genuine error (network, 500, etc.) shows the error text.
 */
export function WorkflowPanel({ token, recordId, onChanged }: WorkflowPanelProps) {
  const { t } = useTranslation();
  const [status, setStatus] = useState<WorkflowStatusDto | null>(null);
  const [hasWorkflow, setHasWorkflow] = useState(true);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useErrorMessage();

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [recordId]);

  async function load() {
    setError(null);
    try {
      const result = await api.workflow.status(token, recordId);
      setStatus(result);
      setHasWorkflow(true);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setHasWorkflow(false);
      } else {
        setError({ err, fallbackKey: 'workflowPanel.loadError' });
      }
    }
  }

  async function executeTransition(transitionCode: string) {
    setBusy(true);
    setError(null);
    try {
      await api.workflow.executeTransition(token, recordId, transitionCode, comment || undefined);
      setComment('');
      await load();
      onChanged?.();
    } catch (err) {
      setError({ err, fallbackKey: 'workflowPanel.actionError' });
    } finally {
      setBusy(false);
    }
  }

  if (!hasWorkflow) return null;
  if (error) return <p className="text-sm text-danger">{error}</p>;
  if (!status) return <p className="text-xs uppercase tracking-wide text-ink-soft">{t('workflowPanel.loadingStatus')}</p>;

  return (
    <div className="border border-border rounded bg-panel p-4 shadow-recessed">
      <div className="mb-3 flex items-center justify-between">
        <span className="text-[11px] uppercase tracking-wider text-ink-soft">{t('workflowPanel.status')}</span>
        <StatusLed label={status.currentStateLabel} tone={status.isFinal ? 'success' : 'accent'} />
      </div>

      {status.availableTransitions.length > 0 && (
        <div className="space-y-2 border-t border-border rounded pt-3">
          <input
            type="text"
            placeholder={t('workflowPanel.commentPlaceholder')}
            className="w-full border border-border rounded bg-bg px-2 py-1.5 text-sm focus:border-accent focus:outline-none"
            value={comment}
            onChange={(e) => setComment(e.target.value)}
          />
          <div className="flex flex-wrap gap-2">
            {status.availableTransitions.map((tr) => (
              <button
                key={tr.code}
                disabled={busy}
                onClick={() => executeTransition(tr.code)}
                className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
              >
                {tr.label}
              </button>
            ))}
          </div>
        </div>
      )}

      {status.availableTransitions.length === 0 && !status.isFinal && (
        <p className="border-t border-border rounded pt-3 text-sm text-ink-soft">{t('workflowPanel.noActionAvailable')}</p>
      )}

      {status.history.length > 0 && (
        <details className="mt-3 border-t border-border rounded pt-3">
          <summary className="cursor-pointer text-[11px] uppercase tracking-wider text-ink-soft">
            {t('workflowPanel.history', { count: status.history.length })}
          </summary>
          <ul className="mt-2 space-y-1.5">
            {status.history.map((h, i) => (
              <li key={i} className="font-mono text-xs text-ink-soft">
                <span className="text-ink">
                  <bdi>{h.fromStateLabel ?? t('workflowPanel.started')}</bdi> → <bdi>{h.toStateLabel}</bdi>
                </span>
                {h.transitionLabel ? ` · ${h.transitionLabel}` : ''} · {new Date(h.executedAtUtc).toLocaleString()}
                {h.comment ? <span className="block italic">"{h.comment}"</span> : null}
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}
