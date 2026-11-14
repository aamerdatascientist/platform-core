import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Icon } from '../icons';
import { useModalFocus } from '../ui';
import { useNav, useShell } from './ShellData';
import { useLocalized } from '../../forms/localized';

interface Entry {
  id: string;
  label: string;
  group: string;
  run: () => void;
}

/**
 * Search / jump (HANDOFF §5.2): every form and page the person can open, filtered as they
 * type. Ctrl+K / Cmd+K opens it from anywhere; arrows move, Enter opens, Esc closes.
 */
export function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { openDashboard } = useShell();
  const groups = useNav();
  const { formName } = useLocalized();
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const ref = useRef<HTMLDivElement>(null);
  const listId = useId();
  useModalFocus(open, ref, onClose);

  useEffect(() => {
    if (open) {
      setQuery('');
      setActive(0);
    }
  }, [open]);

  const entries = useMemo<Entry[]>(() => {
    const go = (path: string) => () => navigate(path);
    const pages: Entry[] = [
      { id: 'today', label: t('nav.todayTasks'), group: t('nav.main'), run: go('/') },
      { id: 'entries', label: t('nav.myEntries'), group: t('nav.main'), run: go('/entries') },
      { id: 'settings', label: t('nav.settings'), group: t('nav.main'), run: go('/settings') },
    ];
    // Every menu item, in the menu's groups and order - Advanced setup included even while it is
    // collapsed in the side menu (F5-8).
    const items = groups.flatMap((g) =>
      g.items.map<Entry>((i) =>
        i.kind === 'form'
          ? { id: i.form.id, label: formName(i.form), group: g.label, run: go(`/forms/${i.form.id}`) }
          : { id: i.id, label: t(i.labelKey), group: g.label, run: i.to === null ? openDashboard : go(i.to) },
      ),
    );
    return [...pages, ...items];
  }, [groups, navigate, openDashboard, t, formName]);

  const q = query.trim().toLowerCase();
  const shown = q ? entries.filter((e) => e.label.toLowerCase().includes(q) || e.group.toLowerCase().includes(q)) : entries;
  if (!open) return null;

  const choose = (entry: Entry | undefined) => {
    if (!entry) return;
    onClose();
    entry.run();
  };

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-start justify-center p-6 pt-[12vh]">
      <div className="absolute inset-0 animate-[scrim-in_160ms_ease-out]" style={{ background: 'var(--scrim)' }} onClick={onClose} aria-hidden="true" />
      <div ref={ref} role="dialog" aria-modal="true" aria-label={t('nav.searchOrJump')} className="relative flex max-h-[70vh] w-full max-w-[560px] flex-col overflow-hidden rounded-ui-2xl border border-border bg-panel">
        <div className="flex items-center gap-2 border-b border-divider px-4">
          <Icon name="search" size={20} className="text-ink-soft" />
          <input
            data-autofocus
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            aria-activedescendant={shown[active] ? `${listId}-${shown[active].id}` : undefined}
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setActive(0);
            }}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') {
                e.preventDefault();
                setActive((i) => Math.min(i + 1, shown.length - 1));
              } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setActive((i) => Math.max(i - 1, 0));
              } else if (e.key === 'Enter') {
                e.preventDefault();
                choose(shown[active]);
              }
            }}
            placeholder={t('nav.searchPlaceholder')}
            aria-label={t('nav.searchOrJump')}
            className="h-[56px] min-w-0 flex-1 bg-transparent text-[16px] text-ink placeholder:text-ink-soft focus:outline-none"
          />
        </div>
        <ul id={listId} role="listbox" className="overflow-y-auto p-2">
          {shown.length === 0 && <li className="px-3 py-6 text-center text-[14px] text-ink-soft">{t('nav.noMatches', { q: query })}</li>}
          {shown.map((entry, i) => (
            <li
              key={entry.id}
              id={`${listId}-${entry.id}`}
              role="option"
              aria-selected={i === active}
              onMouseEnter={() => setActive(i)}
              onClick={() => choose(entry)}
              className={`flex min-h-[44px] cursor-pointer items-center justify-between gap-3 rounded-ui-md px-3 ${i === active ? 'bg-accent-soft text-accent-strong' : 'text-ink'}`}
            >
              <span className="truncate text-[15px] font-medium"><bdi>{entry.label}</bdi></span>
              <span className="shrink-0 text-[12px] text-ink-soft">{entry.group}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>,
    document.body,
  );
}

/** Ctrl+K / Cmd+K anywhere opens the palette. */
export function useCommandPaletteShortcut(onOpen: () => void, enabled: boolean) {
  useEffect(() => {
    if (!enabled) return;
    const onKey = (e: KeyboardEvent) => {
      // e.code too: on an Arabic keyboard layout the K key types a different letter.
      if ((e.ctrlKey || e.metaKey) && (e.code === 'KeyK' || e.key.toLowerCase() === 'k')) {
        e.preventDefault();
        onOpen();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onOpen, enabled]);
}
