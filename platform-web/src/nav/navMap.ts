/**
 * The one place the menus are grouped (admin flows spec F5-1/F5-2, 2026-11-14): groups follow an
 * administrator's jobs, not the forms' modules. The side menu, Ctrl/Cmd+K, the Forms page (phone
 * tab), Settings > Administration and Today's "Other forms" all read this. A navigation-layer map
 * keyed by form code and admin page; a form's module is left alone (the API, analytics and the
 * builder's form picker use it). Pure and import-free so `node --test` can run it (tests/).
 */

export type NavRouteId = 'stageProgress' | 'users' | 'assignments' | 'messages' | 'reports' | 'overview' | 'builder';

export type NavItemSpec = { kind: 'form'; code: string } | { kind: 'route'; id: NavRouteId };

export interface NavGroupSpec {
  id: string;
  labelKey: string;
  /** Collapsed by default (Advanced setup, F5-7). */
  collapsible?: boolean;
  items: NavItemSpec[];
}

const form = (code: string): NavItemSpec => ({ kind: 'form', code });
const route = (id: NavRouteId): NavItemSpec => ({ kind: 'route', id });

export const NAV_GROUPS: NavGroupSpec[] = [
  { id: 'daily', labelKey: 'navGroups.dailyWork', items: [form('daily-progress-report'), form('stock-inflow'), form('stock-outflow')] },
  {
    id: 'projects',
    labelKey: 'navGroups.projects',
    items: [form('projects'), form('project-floors'), form('stage-plan-project'), route('stageProgress'), form('project-pause-log')],
  },
  { id: 'team', labelKey: 'navGroups.team', items: [route('users'), route('assignments'), route('messages'), route('reports')] },
  { id: 'overview', labelKey: 'navGroups.overview', items: [route('overview')] },
  { id: 'advanced', labelKey: 'navGroups.advanced', collapsible: true, items: [route('builder'), form('stage-plan-default')] },
];

/** Admin pages: address (null = opens in a new tab), label key. All are administrators-only. */
export const NAV_ROUTES: Record<NavRouteId, { to: string | null; labelKey: string }> = {
  stageProgress: { to: '/admin/projects', labelKey: 'nav.stageProgress' },
  users: { to: '/admin/users', labelKey: 'userManagement.title' },
  assignments: { to: '/admin/assignments', labelKey: 'settings.assignments' },
  messages: { to: '/admin/messages', labelKey: 'messages.title' },
  reports: { to: '/admin/reports', labelKey: 'reports.title' },
  overview: { to: null, labelKey: 'sidebar.executiveOverview' },
  builder: { to: '/builder', labelKey: 'settings.formBuilder' },
};

/** Settings > Administration on a phone (F5-9). */
export const PHONE_ADMIN_ORDER: NavRouteId[] = ['users', 'assignments', 'messages', 'reports', 'overview', 'stageProgress', 'builder'];

/** Unmapped forms (e.g. one the client builds later) go in a group named after their module, after Projects (F5-3). */
const FALLBACK_AFTER = 'projects';

export interface NavForm {
  id: string;
  code: string;
  moduleName: string;
  status: string;
  isHidden?: boolean;
}

export type NavItem<F> = { kind: 'form'; form: F } | { kind: 'route'; id: NavRouteId; to: string | null; labelKey: string };

export interface NavGroup<F> {
  id: string;
  /** A translation key, or null for a module group (label = the module name). */
  labelKey: string | null;
  moduleName: string | null;
  collapsible: boolean;
  items: NavItem<F>[];
}

/**
 * The groups this person sees, in order. `forms` is what the API says the person may open (it
 * already applies form access); hidden and unpublished forms never show; admin pages only for
 * administrators; empty groups are dropped (F5-3).
 */
export function buildNav<F extends NavForm>(forms: readonly F[], isAdmin: boolean): NavGroup<F>[] {
  const visible = forms.filter((f) => f.status === 'Published' && !f.isHidden);
  const byCode = new Map(visible.map((f) => [f.code, f]));
  const mapped = new Set(NAV_GROUPS.flatMap((g) => g.items.flatMap((i) => (i.kind === 'form' ? [i.code] : []))));

  const groups: NavGroup<F>[] = [];
  for (const spec of NAV_GROUPS) {
    const items: NavItem<F>[] = [];
    for (const item of spec.items) {
      if (item.kind === 'form') {
        const f = byCode.get(item.code);
        if (f) items.push({ kind: 'form', form: f });
      } else if (isAdmin) {
        items.push({ kind: 'route', id: item.id, ...NAV_ROUTES[item.id] });
      }
    }
    groups.push({ id: spec.id, labelKey: spec.labelKey, moduleName: null, collapsible: !!spec.collapsible, items });

    if (spec.id === FALLBACK_AFTER) {
      const modules = new Map<string, F[]>();
      for (const f of visible) if (!mapped.has(f.code)) modules.set(f.moduleName, [...(modules.get(f.moduleName) ?? []), f]);
      for (const [moduleName, list] of modules)
        groups.push({ id: `module:${moduleName}`, labelKey: null, moduleName, collapsible: false, items: list.map((f) => ({ kind: 'form', form: f })) });
    }
  }
  return groups.filter((g) => g.items.length > 0);
}

/** The forms in menu order, Advanced setup left out (Today's "Other forms", F5-9). */
export function everydayForms<F extends NavForm>(groups: NavGroup<F>[]): F[] {
  return groups.filter((g) => !g.collapsible).flatMap((g) => g.items.flatMap((i) => (i.kind === 'form' ? [i.form] : [])));
}

/** True when `pathname` is one of the group's pages - the collapsed group opens itself there (F5-7). */
export function groupContainsPath<F extends NavForm>(group: NavGroup<F>, pathname: string): boolean {
  return group.items.some((i) =>
    i.kind === 'form' ? pathname.startsWith(`/forms/${i.form.id}`) : i.to !== null && (pathname === i.to || pathname.startsWith(`${i.to}/`)),
  );
}
