import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { FormSummaryDto } from '../types';

function slugify(input: string): string {
  return input.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

export function BuilderHome({ token }: { token: string }) {
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [codeTouched, setCodeTouched] = useState(false);
  const [moduleName, setModuleName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    load();
  }, []);

  async function load() {
    const result = await api.forms.list(token);
    setForms(result);
  }

  function handleNameChange(value: string) {
    setName(value);
    if (!codeTouched) setCode(slugify(value));
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim() || !code.trim() || !moduleName.trim()) {
      setError('Name, code, and module are all required.');
      return;
    }
    setSubmitting(true);
    try {
      const result = await api.forms.create(token, { code, name, moduleName });
      navigate(`/builder/${result.id}`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create that form.');
    } finally {
      setSubmitting(false);
    }
  }

  const existingModules = [...new Set((forms ?? []).map((f) => f.moduleName))];
  const inputClass = 'w-full border border-line px-2 py-1.5 text-sm focus:border-ink focus:outline-none';

  return (
    <div className="max-w-3xl space-y-8">
      <div>
        <h2 className="font-display text-xl font-semibold text-ink">Build forms</h2>
        <p className="text-sm text-ink-muted">Define a new form, or open an existing one to add fields.</p>
      </div>

      {!creating ? (
        <button
          onClick={() => setCreating(true)}
          className="border border-line bg-white px-3 py-1.5 text-sm text-ink hover:border-ink"
        >
          + New form
        </button>
      ) : (
        <form onSubmit={handleCreate} className="space-y-3 border border-line bg-white p-4">
          <div>
            <label className="mb-1 block text-xs text-ink-muted">Form name</label>
            <input className={inputClass} value={name} onChange={(e) => handleNameChange(e.target.value)} placeholder="Delivery inspection" />
          </div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div>
              <label className="mb-1 block text-xs text-ink-muted">Code</label>
              <input
                className={`${inputClass} font-mono`}
                value={code}
                onChange={(e) => {
                  setCode(e.target.value);
                  setCodeTouched(true);
                }}
              />
            </div>
            <div>
              <label className="mb-1 block text-xs text-ink-muted">Module</label>
              <input
                className={inputClass}
                list="module-suggestions"
                value={moduleName}
                onChange={(e) => setModuleName(e.target.value)}
                placeholder="StockManagement"
              />
              <datalist id="module-suggestions">
                {existingModules.map((m) => (
                  <option key={m} value={m} />
                ))}
              </datalist>
            </div>
          </div>
          {error && <p className="text-sm text-clay">{error}</p>}
          <div className="flex gap-2">
            <button
              type="submit"
              disabled={submitting}
              className="bg-ink px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
            >
              {submitting ? 'Creating…' : 'Create and add fields'}
            </button>
            <button type="button" onClick={() => setCreating(false)} className="px-3 py-1.5 text-sm text-ink-muted">
              Cancel
            </button>
          </div>
        </form>
      )}

      <div>
        <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-muted">Existing forms</h3>
        {!forms ? (
          <p className="text-xs uppercase tracking-wide text-ink-muted">Loading…</p>
        ) : forms.length === 0 ? (
          <p className="text-sm text-ink-muted">No forms yet - create the first one above.</p>
        ) : (
          <div className="space-y-1">
            {forms.map((f) => (
              <button
                key={f.id}
                onClick={() => navigate(`/builder/${f.id}`)}
                className="flex w-full items-center justify-between border border-line bg-white px-3 py-2 text-left text-sm hover:border-ink"
              >
                <span>
                  <span className="font-medium text-ink">{f.name}</span>
                  <span className="ml-2 text-xs text-ink-muted">{f.moduleName}</span>
                </span>
                <span className="text-[11px] uppercase tracking-wide text-ink-muted">{f.status}</span>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
