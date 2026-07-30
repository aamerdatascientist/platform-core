import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { FieldDefinitionDto, FileMetadataDto } from '../types';

interface AttachmentsPanelProps {
  token: string;
  formId: string;
  recordId: string;
  attachmentFields: FieldDefinitionDto[];
}

export function AttachmentsPanel({ token, formId, recordId, attachmentFields }: AttachmentsPanelProps) {
  const [files, setFiles] = useState<FileMetadataDto[] | null>(null);
  const [selectedField, setSelectedField] = useState(attachmentFields[0]?.code ?? '');
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [recordId]);

  async function load() {
    try {
      setFiles(await api.files.listForRecord(token, recordId));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load attachments.');
    }
  }

  async function handleFileSelected(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file || !selectedField) return;

    setUploading(true);
    setError(null);
    try {
      await api.files.upload(token, formId, recordId, selectedField, file);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Upload failed.');
    } finally {
      setUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  }

  async function handleView(fileId: string) {
    try {
      const { url } = await api.files.getDownloadUrl(token, fileId);
      window.open(url, '_blank', 'noopener,noreferrer');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not open that file.');
    }
  }

  async function handleDelete(fileId: string) {
    try {
      await api.files.delete(token, fileId);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete that file.');
    }
  }

  if (attachmentFields.length === 0) return null;

  return (
    <div className="border border-line bg-white p-4">
      <span className="mb-3 block font-mono text-[11px] uppercase tracking-wider text-ink-muted">Attachments</span>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        {attachmentFields.length > 1 && (
          <select
            className="border border-line px-2 py-1.5 text-sm focus:border-ink focus:outline-none"
            value={selectedField}
            onChange={(e) => setSelectedField(e.target.value)}
          >
            {attachmentFields.map((f) => (
              <option key={f.code} value={f.code}>
                {f.label}
              </option>
            ))}
          </select>
        )}
        <input ref={fileInputRef} type="file" accept="image/*,application/pdf" onChange={handleFileSelected} disabled={uploading} className="text-sm" />
        {uploading && <span className="font-mono text-xs text-ink-muted">Uploading…</span>}
      </div>

      {error && <p className="mb-2 text-sm text-clay">{error}</p>}

      {!files ? (
        <p className="font-mono text-xs uppercase tracking-wide text-ink-muted">Loading…</p>
      ) : files.length === 0 ? (
        <p className="text-sm text-ink-muted">No files attached yet.</p>
      ) : (
        <ul className="space-y-1.5">
          {files.map((f) => (
            <li key={f.id} className="flex items-center justify-between border border-line px-3 py-2 text-sm">
              <button onClick={() => handleView(f.id)} className="truncate text-left text-ink underline decoration-line hover:decoration-ink">
                {f.originalFileName}
              </button>
              <span className="ml-3 flex shrink-0 items-center gap-3">
                <span className="font-mono text-xs text-ink-muted">{formatSize(f.sizeBytes)}</span>
                <button onClick={() => handleDelete(f.id)} className="font-mono text-[11px] uppercase tracking-wide text-clay hover:opacity-70">
                  Remove
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
