import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
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
  const [status, setStatus] = useState<WorkflowStatusDto | null>(null);
  const [hasWorkflow, setHasWorkflow] = useState(true);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
        setError(err instanceof ApiError ? err.message : 'Could not load workflow status.');
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
      setError(err instanceof ApiError ? err.message : 'That action could not be completed.');
    } finally {
      setBusy(false);
    }
  }

  if (!hasWorkflow) return null;
  if (error) return <p className="text-sm text-clay">{error}</p>;
  if (!status) return <p className="font-mono text-xs uppercase tracking-wide text-ink-muted">Loading status…</p>;

  return (
    <div className="border border-line bg-white p-4">
      <div className="mb-3 flex items-center justify-between">
        <span className="font-mono text-[11px] uppercase tracking-wider text-ink-muted">Workflow status</span>
        <StatusBadge label={status.currentStateLabel} isFinal={status.isFinal} />
      </div>

      {status.availableTransitions.length > 0 && (
        <div className="space-y-2 border-t border-line pt-3">
          <input
            type="text"
            placeholder="Add a comment (optional)"
            className="w-full border border-line px-2 py-1.5 text-sm focus:border-ink focus:outline-none"
            value={comment}
            onChange={(e) => setComment(e.target.value)}
          />
          <div className="flex flex-wrap gap-2">
            {status.availableTransitions.map((t) => (
              <button
                key={t.code}
                disabled={busy}
                onClick={() => executeTransition(t.code)}
                className="bg-ink px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
              >
                {t.label}
              </button>
            ))}
          </div>
        </div>
      )}

      {status.availableTransitions.length === 0 && !status.isFinal && (
        <p className="border-t border-line pt-3 text-sm text-ink-muted">
          No action available to you at this stage - you may not hold the role required to act on it.
        </p>
      )}

      {status.history.length > 0 && (
        <details className="mt-3 border-t border-line pt-3">
          <summary className="cursor-pointer font-mono text-[11px] uppercase tracking-wider text-ink-muted">
            History ({status.history.length})
          </summary>
          <ul className="mt-2 space-y-1.5">
            {status.history.map((h, i) => (
              <li key={i} className="font-mono text-xs text-ink-muted">
                <span className="text-ink">{h.fromStateLabel ?? 'Started'} → {h.toStateLabel}</span>
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

function StatusBadge({ label, isFinal }: { label: string; isFinal: boolean }) {
  const color = isFinal ? 'text-moss border-moss' : 'text-signal-dark border-signal-dark';
  const dot = isFinal ? 'bg-moss' : 'bg-signal-dark';
  return (
    <span className={`inline-flex items-center gap-1.5 border px-2 py-0.5 font-mono text-[11px] uppercase tracking-wider ${color}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dot}`} />
      {label}
    </span>
  );
}
