import type { DynamicRow, FieldDefinitionDto } from '../types';

interface SubmissionsTableProps {
  fields: FieldDefinitionDto[];
  rows: DynamicRow[];
}

export function SubmissionsTable({ fields, rows }: SubmissionsTableProps) {
  const columns = fields.filter((f) => f.isActive && f.fieldType !== 'Attachment');

  if (rows.length === 0) {
    return <p className="text-sm text-gray-400">No submissions yet.</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-gray-200 text-gray-500">
            {columns.map((c) => (
              <th key={c.id} className="px-3 py-2 font-medium">
                {c.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} className="border-b border-gray-100">
              {columns.map((c) => (
                <td key={c.id} className="px-3 py-2">
                  {formatValue(row.values[c.code])}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value);
}
