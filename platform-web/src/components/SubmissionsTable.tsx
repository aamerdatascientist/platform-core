import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import type { DynamicRow, FieldDefinitionDto } from '../types';

interface SubmissionsTableProps {
  fields: FieldDefinitionDto[];
  rows: DynamicRow[];
  onRowClick?: (id: string) => void;
  selectedRecordId?: string | null;
}

export function SubmissionsTable({ fields, rows, onRowClick, selectedRecordId }: SubmissionsTableProps) {
  const { t } = useTranslation();
  const columns = fields.filter((f) => f.isActive && f.fieldType !== 'Attachment');

  if (rows.length === 0) {
    return <p className="text-sm text-ink-muted">{t('submissionsTable.noRecords')}</p>;
  }

  return (
    <div className="overflow-x-auto border border-line">
      <table className="w-full text-start text-sm">
        <thead>
          <tr className="border-b border-line bg-paper">
            {columns.map((c) => (
              <th key={c.id} className="px-3 py-2 text-[11px] font-medium uppercase tracking-wider text-ink-muted">
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
              className={`border-b border-line last:border-0 ${onRowClick ? 'cursor-pointer hover:bg-paper' : ''} ${
                row.id === selectedRecordId ? 'bg-paper' : ''
              }`}
            >
              {columns.map((c) => (
                <td key={c.id} className="px-3 py-2">
                  {formatValue(row.values[c.code], t)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function formatValue(value: unknown, t: TFunction): string {
  if (value === null || value === undefined || value === '') return t('common.empty');
  if (typeof value === 'boolean') return value ? t('common.yes') : t('common.no');
  return String(value);
}
