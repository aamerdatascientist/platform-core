import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';

interface ProjectOption {
  id: string;
  code: string;
  name: string;
}

interface ProjectSelectorProps {
  token: string;
  value: string | null;
  onChange: (projectId: string | null) => void;
}

/**
 * Resolves the Projects form id internally (by Code, via the forms list - nothing hardcodes
 * its GUID) rather than taking it as a prop, so any future dashboard can drop this in
 * without first having to go fetch that id itself. Mirrors FormRenderer's own Lookup-choice
 * fetch: Projects is small master data, one page comfortably covers it.
 */
export function ProjectSelector({ token, value, onChange }: ProjectSelectorProps) {
  const { t } = useTranslation();
  const [options, setOptions] = useState<ProjectOption[]>([]);
  const [error, setError] = useState(false);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const forms = await api.forms.list(token, 'Operations');
        const projectsForm = forms.find((f) => f.code === 'projects');
        if (!projectsForm) {
          if (!cancelled) setError(true);
          return;
        }

        const page = await api.submissions.list(token, projectsForm.id, 1, 200);
        if (cancelled) return;

        setOptions(
          page.items
            .map((row) => ({
              id: row.id,
              code: String(row.values.project_code ?? ''),
              name: String(row.values.project_name ?? ''),
            }))
            .sort((a, b) => a.code.localeCompare(b.code)),
        );
      } catch {
        if (!cancelled) setError(true);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [token]);

  if (error) return null;

  return (
    <select
      className="w-full max-w-xs rounded border border-border bg-bg px-3 py-2 text-sm focus:border-accent focus:outline-none sm:w-auto"
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value || null)}
    >
      <option value="">{t('projectSelector.allProjects')}</option>
      {options.map((option) => (
        <option key={option.id} value={option.id}>
          {option.code} — {option.name}
        </option>
      ))}
    </select>
  );
}
