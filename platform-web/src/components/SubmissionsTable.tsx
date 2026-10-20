import { useEffect, useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { formatCalendarDate, parseOptions, storedToSaudiDate } from '../forms/fieldValues';
import type { DynamicRow, FieldDefinitionDto } from '../types';

interface SubmissionsTableProps {
  token: string;
  fields: FieldDefinitionDto[];
  rows: DynamicRow[];
  onRowClick?: (id: string) => void;
  selectedRecordId?: string | null;
  /** Paging: the table shows `rows` (one page) and Previous/Next when there are more. */
  page?: number;
  pageSize?: number;
  totalCount?: number;
  onPageChange?: (page: number) => void;
}

/**
 * Resolves Lookup columns to a human-readable label the same way FormRenderer resolves
 * Lookup choices for its own dropdown - fetch the target form's display field (first active
 * ShortText field, same "known simplification" FormRenderer's own doc comment describes),
 * then its submissions, and map raw referenced Ids to that label. Without this, a Lookup
 * column just showed the raw GUID (formatValue's plain String(value) fallback), which is
 * meaningless to anyone reading the table.
 */
export function SubmissionsTable({
  token,
  fields,
  rows,
  onRowClick,
  selectedRecordId,
  page = 1,
  pageSize = rows.length,
  totalCount = rows.length,
  onPageChange,
}: SubmissionsTableProps) {
  const { t, i18n } = useTranslation();
  const columns = fields.filter((f) => f.isActive && f.fieldType !== 'Attachment');
  const [lookupLabelsByField, setLookupLabelsByField] = useState<Record<string, Record<string, string>>>({});

  useEffect(() => {
    const lookupFields = columns.filter((f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId);
    const uniqueTargets = [...new Set(lookupFields.map((f) => f.lookupFormDefinitionId as string))];

    uniqueTargets.forEach(async (targetFormId) => {
      try {
        const targetDef = await api.forms.get(token, targetFormId);
        const displayField = targetDef.publishedVersion?.fields.find((f) => f.isActive && f.fieldType === 'ShortText');

        // Every row of the target form, a page (200, the server's maximum) at a time.
        const labelsById: Record<string, string> = {};
        for (let targetPage = 1; targetPage <= 25; targetPage++) {
          const result = await api.submissions.list(token, targetFormId, targetPage, 200);
          for (const row of result.items) {
            labelsById[row.id] = displayField ? String(row.values[displayField.code] ?? row.id) : row.id;
          }
          if (result.items.length === 0 || targetPage * 200 >= result.totalCount) break;
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

  if (rows.length === 0 && page <= 1) {
    return <p className="text-sm text-ink-soft">{t('submissionsTable.noRecords')}</p>;
  }

  const firstShown = (page - 1) * pageSize + 1;
  const lastShown = (page - 1) * pageSize + rows.length;
  const pageButton =
    'rounded border border-border bg-panel px-3 py-1 text-xs text-ink hover:bg-bg disabled:cursor-not-allowed disabled:opacity-40';

  return (
    <div>
    <div className="overflow-x-auto border border-border rounded shadow-recessed">
      <table className="w-full text-start text-sm">
        <thead>
          <tr className="border-b border-border rounded bg-bg">
            {columns.map((c) => (
              <th key={c.id} className="px-3 py-2 text-label font-medium uppercase tracking-wider text-ink-soft">
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
                  {formatValue(row.values[c.code], c, lookupLabelsByField[c.code], t, i18n.language)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
      {onPageChange && totalCount > pageSize && (
        <div className="mt-2 flex items-center justify-between gap-2 text-xs text-ink-soft">
          <span>{t('submissionsTable.showing', { from: firstShown, to: lastShown, total: totalCount })}</span>
          <div className="flex gap-2">
            <button type="button" className={pageButton} disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
              {t('submissionsTable.previous')}
            </button>
            <button type="button" className={pageButton} disabled={lastShown >= totalCount} onClick={() => onPageChange(page + 1)}>
              {t('submissionsTable.next')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function formatValue(
  value: unknown,
  field: FieldDefinitionDto,
  lookupLabelsById: Record<string, string> | undefined,
  t: TFunction,
  language: string,
): string {
  if (value === null || value === undefined || value === '') return t('common.empty');
  if (typeof value === 'boolean') return value ? t('common.yes') : t('common.no');
  if (field.fieldType === 'Lookup') return lookupLabelsById?.[String(value)] ?? String(value);
  if (field.fieldType === 'DateTime') {
    // Stored as Saudi midnight in UTC - shown as the Saudi calendar day it was entered as.
    const date = storedToSaudiDate(value);
    return date ? formatCalendarDate(date, language) : String(value);
  }
  if (field.fieldType === 'Dropdown' && !field.dynamicOptionsSourceFormDefinitionId) {
    // The option's label, not its stored code ("excavation" -> "Excavation / حفر").
    return parseOptions(field.optionsJson).find((o) => o.value === String(value))?.label ?? String(value);
  }
  return String(value);
}
