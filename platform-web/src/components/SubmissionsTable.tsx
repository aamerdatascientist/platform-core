import { useEffect, useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import type { DynamicRow, FieldDefinitionDto } from '../types';

interface SubmissionsTableProps {
  token: string;
  fields: FieldDefinitionDto[];
  rows: DynamicRow[];
  onRowClick?: (id: string) => void;
  selectedRecordId?: string | null;
}

/**
 * Resolves Lookup columns to a human-readable label the same way FormRenderer resolves
 * Lookup choices for its own dropdown - fetch the target form's display field (first active
 * ShortText field, same "known simplification" FormRenderer's own doc comment describes),
 * then its submissions, and map raw referenced Ids to that label. Without this, a Lookup
 * column just showed the raw GUID (formatValue's plain String(value) fallback), which is
 * meaningless to anyone reading the table.
 */
export function SubmissionsTable({ token, fields, rows, onRowClick, selectedRecordId }: SubmissionsTableProps) {
  const { t } = useTranslation();
  const columns = fields.filter((f) => f.isActive && f.fieldType !== 'Attachment');
  const [lookupLabelsByField, setLookupLabelsByField] = useState<Record<string, Record<string, string>>>({});

  useEffect(() => {
    const lookupFields = columns.filter((f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId);
    const uniqueTargets = [...new Set(lookupFields.map((f) => f.lookupFormDefinitionId as string))];

    uniqueTargets.forEach(async (targetFormId) => {
      try {
        const targetDef = await api.forms.get(token, targetFormId);
        const displayField = targetDef.publishedVersion?.fields.find((f) => f.isActive && f.fieldType === 'ShortText');

        const page = await api.submissions.list(token, targetFormId, 1, 200);
        const labelsById: Record<string, string> = {};
        for (const row of page.items) {
          labelsById[row.id] = displayField ? String(row.values[displayField.code] ?? row.id) : row.id;
        }

        setLookupLabelsByField((prev) => {
          const next = { ...prev };
          lookupFields
            .filter((f) => f.lookupFormDefinitionId === targetFormId)
            .forEach((f) => {
              next[f.code] = labelsById;
            });
          return next;
        });
      } catch {
        // A failed lookup fetch shouldn't block the rest of the table from rendering -
        // those columns just fall back to the raw Id, same as before this fix.
      }
    });
    // columns is derived from the fields prop on every render, not a stable reference -
    // depend on the fields array itself instead to avoid re-fetching every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fields, token]);

  if (rows.length === 0) {
    return <p className="text-sm text-ink-soft">{t('submissionsTable.noRecords')}</p>;
  }

  return (
    <div className="overflow-x-auto border border-border rounded shadow-recessed">
      <table className="w-full text-start text-sm">
        <thead>
          <tr className="border-b border-border rounded bg-bg">
            {columns.map((c) => (
              <th key={c.id} className="px-3 py-2 text-[11px] font-medium uppercase tracking-wider text-ink-soft">
                {c.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={row.id}
              onClick={() => onRowClick?.(row.id)}
              className={`border-b border-border rounded last:border-0 ${onRowClick ? 'cursor-pointer hover:bg-bg' : ''} ${
                row.id === selectedRecordId ? 'bg-bg' : ''
              }`}
            >
              {columns.map((c) => (
                <td key={c.id} className="px-3 py-2">
                  {formatValue(row.values[c.code], c, lookupLabelsByField[c.code], t)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function formatValue(
  value: unknown,
  field: FieldDefinitionDto,
  lookupLabelsById: Record<string, string> | undefined,
  t: TFunction,
): string {
  if (value === null || value === undefined || value === '') return t('common.empty');
  if (typeof value === 'boolean') return value ? t('common.yes') : t('common.no');
  if (field.fieldType === 'Lookup') return lookupLabelsById?.[String(value)] ?? String(value);
  return String(value);
}
