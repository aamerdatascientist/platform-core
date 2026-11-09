import { useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from '../icons';
import { Sheet } from '../ui';

export interface Choice {
  value: string;
  label: string;
  /** A second line under the name (13px), when there is one. */
  meta?: string;
}

/**
 * The choose-from-a-list sheet (screen 04): every Lookup and every long dropdown opens it. A
 * 50px search field, "Recent" (the person's last 3 choices of this field) then "All (n)", 64px
 * rows, the chosen row on --accent-soft with a check, and "Can't find it? Ask an administrator
 * to add it." at the bottom. A bottom sheet on a phone, a 480px dialog on a desktop.
 */
export function LookupSheet({
  open,
  onClose,
  title,
  titleText,
  choices,
  value,
  recent,
  onPick,
}: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  /** The title as plain text, for the list's accessible name. */
  titleText: string;
  choices: Choice[];
  value: string;
  recent: string[];
  onPick: (value: string) => void;
}) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const q = query.trim().toLowerCase();
  const matches = useMemo(
    () => (q ? choices.filter((c) => c.label.toLowerCase().includes(q) || (c.meta ?? '').toLowerCase().includes(q)) : choices),
    [choices, q],
  );
  const recentChoices = q ? [] : recent.map((v) => choices.find((c) => c.value === v)).filter((c): c is Choice => !!c);

  const pick = (v: string) => {
    onPick(v);
    setQuery('');
    onClose();
  };

  const row = (c: Choice, keyPrefix: 'recent' | 'all') => {
    // 04: the chosen row is marked once - under Recent when it is there, otherwise in the full list.
    const on = c.value === value && (keyPrefix === 'recent' || !recentChoices.some((r) => r.value === value));
    return (
      <li key={`${keyPrefix}-${c.value}`} className={keyPrefix === 'all' ? 'border-b border-divider' : ''}>
        <button
          type="button"
          role="option"
          aria-selected={on}
          onClick={() => pick(c.value)}
          className={`flex min-h-[64px] w-full items-center gap-3 px-3 text-start ${keyPrefix === 'recent' ? 'rounded-ui-xl' : ''} ${on ? 'bg-accent-soft' : 'hover:bg-field-muted'}`}
        >
          <span className="flex min-w-0 flex-1 flex-col gap-0.5">
            <span className={`text-[17px] text-ink ${on ? 'font-bold' : 'font-medium'}`}>
              <bdi>{c.label}</bdi>
            </span>
            {c.meta && <span className="text-[13px] text-ink-soft">{c.meta}</span>}
          </span>
          {on && <Icon name="check" size={22} className="text-accent" />}
        </button>
      </li>
    );
  };

  return (
    <Sheet
      open={open}
      onClose={() => {
        setQuery('');
        onClose();
      }}
      title={title}
      footer={<p className="text-center text-[13px] text-ink-soft">{t('lookup.notFound')}</p>}
    >
      <div className="-mx-2">
      <div className="sticky top-0 z-10 bg-panel px-2 pb-3 pt-1.5">
        <label className="flex h-[50px] items-center gap-[10px] rounded-ui-xl bg-field-muted px-[14px]">
          <Icon name="search" size={20} className="text-ink-soft" />
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={t('lookup.search')}
            aria-label={t('lookup.search')}
            className="min-w-0 flex-1 bg-transparent text-[17px] text-ink placeholder:text-ink-soft focus:outline-none"
          />
        </label>
      </div>
      {recentChoices.length > 0 && (
        <>
          <h3 className="p-2 text-[13px] font-bold leading-tight text-ink-soft">{t('lookup.recent')}</h3>
          <ul role="listbox" aria-label={t('lookup.recent')}>
            {recentChoices.map((c) => row(c, 'recent'))}
          </ul>
        </>
      )}
      <h3 className={`px-2 pb-2 text-[13px] font-bold leading-tight text-ink-soft ${recentChoices.length > 0 ? 'pt-4' : 'pt-2'}`}>{t('lookup.all', { count: choices.length })}</h3>
      {matches.length === 0 ? (
        <p className="py-6 text-center text-[15px] text-ink-soft">{t('lookup.noResults', { q: query })}</p>
      ) : (
        <ul role="listbox" aria-label={titleText}>
          {matches.map((c) => row(c, 'all'))}
        </ul>
      )}
      </div>
    </Sheet>
  );
}
