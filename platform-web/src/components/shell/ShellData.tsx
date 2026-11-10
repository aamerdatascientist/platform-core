import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { buildDashboardUrl } from '../../dashboardLink';
import { getMode } from '../../theme/mode';
import type { CurrentUserDto, FormSummaryDto, MyAssignmentsDto } from '../../types';

/**
 * What both shells (and the pages inside them) need about the signed-in person, loaded once:
 * who they are, the forms they can open, and today's assignments (for the "Today · My tasks"
 * badge). Pages that change these (submitting a form) call refresh().
 */
interface ShellData {
  token: string;
  me: CurrentUserDto | null;
  isAdmin: boolean;
  /** Published forms the person can open, in the API's order. */
  forms: FormSummaryDto[] | null;
  formsError: unknown;
  mine: MyAssignmentsDto | null;
  /** Why the person or their assignments couldn't be loaded (Today shows it with Try again). */
  loadError: unknown;
  /** Assignments due today and not filled yet. */
  dueTodayCount: number;
  refresh: () => void;
  /** Opens the Executive overview in a new tab (inside the click, so it isn't blocked as a pop-up). */
  openDashboard: () => void;
  /** A role name as people read it (known system roles are translated; others show as stored). */
  roleLabel: (name: string) => string;
}

const ShellContext = createContext<ShellData | null>(null);

export function ShellProvider({ token, children }: { token: string; children: ReactNode }) {
  const { t, i18n } = useTranslation();
  const [me, setMe] = useState<CurrentUserDto | null>(null);
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [formsError, setFormsError] = useState<unknown>(null);
  const [mine, setMine] = useState<MyAssignmentsDto | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);
  const [dashboardKey, setDashboardKey] = useState<string | null>(null);
  const [version, setVersion] = useState(0);
  const isAdmin = me?.roles.includes('Administrator') ?? false;

  useEffect(() => {
    let cancelled = false;
    setLoadError(null);
    api.auth.me(token).then((m) => !cancelled && setMe(m)).catch((err) => !cancelled && setLoadError(err));
    api.forms
      .list(token)
      .then((f) => !cancelled && (setForms(f), setFormsError(null)))
      .catch((err) => !cancelled && setFormsError(err));
    api.assignments.mine(token).then((m) => !cancelled && setMine(m)).catch((err) => !cancelled && setLoadError(err));
    return () => {
      cancelled = true;
    };
  }, [token, version]);

  // Fetched up front, not on click: a browser only lets a new tab open inside the click itself.
  useEffect(() => {
    if (!isAdmin) return;
    api.analytics
      .executiveOverviewShareKey(token)
      .then((r) => setDashboardKey(r.key))
      .catch(() => setDashboardKey(null));
  }, [token, isAdmin]);

  const refresh = useCallback(() => setVersion((v) => v + 1), []);
  const openDashboard = useCallback(() => {
    window.open(buildDashboardUrl({ shareKey: dashboardKey, language: i18n.language, mode: getMode() }), '_blank');
  }, [dashboardKey, i18n.language]);
  const roleLabel = useCallback((name: string) => t(`roleName.${name}`, { defaultValue: name }), [t]);

  const dueTodayCount = useMemo(
    () => (mine ? mine.days.filter((d) => d.date === mine.today && d.status === 'pending').length : 0),
    [mine],
  );

  const value = useMemo<ShellData>(
    () => ({ token, me, isAdmin, forms, formsError, mine, loadError, dueTodayCount, refresh, openDashboard, roleLabel }),
    [token, me, isAdmin, forms, formsError, mine, loadError, dueTodayCount, refresh, openDashboard, roleLabel],
  );
  return <ShellContext.Provider value={value}>{children}</ShellContext.Provider>;
}

/** The shell data when inside the app shell, or null (e.g. a preview rendered elsewhere). */
export function useOptionalShell(): ShellData | null {
  return useContext(ShellContext);
}

export function useShell(): ShellData {
  const value = useContext(ShellContext);
  if (!value) throw new Error('useShell must be used inside ShellProvider');
  return value;
}

/** Published forms grouped by module, module names translated (as the old sidebar did). */
export function useFormsByModule() {
  const { t } = useTranslation();
  const { forms } = useShell();
  return useMemo(() => {
    const groups = new Map<string, { label: string; forms: FormSummaryDto[] }>();
    for (const form of forms ?? []) {
      // Hidden forms (retired, e.g. project zones) stay out of every menu; data is kept.
      if (form.status !== 'Published' || form.isHidden) continue;
      const group = groups.get(form.moduleName) ?? { label: t(`moduleName.${form.moduleName}`, { defaultValue: form.moduleName }), forms: [] };
      group.forms.push(form);
      groups.set(form.moduleName, group);
    }
    return [...groups.values()];
  }, [forms, t]);
}

/** The first letter of a name, for the avatar circle. */
export function initial(name: string | null | undefined) {
  return (name ?? '').trim().charAt(0).toUpperCase() || '·';
}
