import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useShell } from '../components/shell/ShellData';
import { Button, EmptyState } from '../components/ui';
import { saudiTodayIso } from '../forms/fieldValues';
import { useLocalized } from '../forms/localized';
import { dayLabel, entrySummary, entryTags, localeFor, sentDay, sentTime } from '../forms/recordAnswers';
import type { EntryTag } from '../forms/recordAnswers';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FormDefinitionDto, RecordItemDto } from '../types';

const PILL: Record<EntryTag['tone'], string> = {
  success: 'bg-success-soft text-success',
  danger: 'bg-danger-soft text-danger',
  warning: 'bg-warning-soft text-warning',
  info: 'bg-accent-soft text-accent-strong',
};

/**
 * My entries (screen 08, HANDOFF §9.3): what the person sent, newest first, grouped by day
 * ("Today · Saturday 7 November", then plain dates). Chips filter by form ("All forms" first,
 * then each form they have entries for). A card shows the form, the time, a summary of its
 * telling answers and tag pills; it opens the read-only detail. 20 at a time, more on scroll.
 */
export function MyEntries({ token }: { token: string }) {
  const { t, i18n } = useTranslation();
  const { forms } = useShell();
  const { formName } = useLocalized();
  const locale = localeFor(i18n.language);
  const ctx = useMemo(() => ({ t, lang: i18n.language, locale }), [t, i18n.language, locale]);

  const [formFilter, setFormFilter] = useState<string | null>(null);
  const [items, setItems] = useState<RecordItemDto[] | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useErrorMessage();
  const [seenForms, setSeenForms] = useState<string[]>([]);
  const [defs, setDefs] = useState<Record<string, FormDefinitionDto>>({});
  const sentinel = useRef<HTMLDivElement>(null);

  const load = useCallback(
    async (reset: boolean) => {
      setLoading(true);
      setError(null);
      try {
        const page = await api.submissions.mine(token, { formId: formFilter, before: reset ? null : next });
        setItems((prev) => (reset ? page.items : [...(prev ?? []), ...page.items]));
        setNext(page.nextBefore);
        setSeenForms((prev) => [...new Set([...prev, ...page.items.map((i) => i.formId)])]);
      } catch (err) {
        setError({ err, fallbackKey: 'entries.loadError' });
        if (reset) setItems([]);
      } finally {
        setLoading(false);
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [token, formFilter, next],
  );

  useEffect(() => {
    setItems(null);
    setNext(null);
    void load(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formFilter, token]);

  // The form definitions behind the cards (labels, options) - fetched once per form.
  useEffect(() => {
    for (const id of seenForms) {
      if (defs[id]) continue;
      api.forms
        .get(token, id)
        .then((d) => setDefs((prev) => ({ ...prev, [id]: d })))
        .catch(() => {});
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [seenForms, token]);

  // Infinite scroll: the next 20 when the end of the list comes into view.
  useEffect(() => {
    const el = sentinel.current;
    if (!el || !next || loading) return;
    const observer = new IntersectionObserver((entries) => entries[0]?.isIntersecting && void load(false), { rootMargin: '200px' });
    observer.observe(el);
    return () => observer.disconnect();
  }, [next, loading, load]);

  const today = saudiTodayIso();
  const dayTitle = (day: string) => {
    const label = dayLabel(day, locale);
    return day === today ? t('entries.todayHeading', { date: label }) : label;
  };
  const groups = useMemo(() => {
    const map = new Map<string, RecordItemDto[]>();
    for (const item of items ?? []) map.set(sentDay(item), [...(map.get(sentDay(item)) ?? []), item]);
    return [...map.entries()];
  }, [items]);
  const nameOf = (formId: string) => {
    const summary = forms?.find((f) => f.id === formId) ?? defs[formId];
    return summary ? formName(summary) : '';
  };

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-4">
      <h1 className="text-[26px] font-extrabold text-ink">{t('entries.title')}</h1>

      {seenForms.length > 0 && (
        <div className="-mx-5 flex gap-2 overflow-x-auto px-5 pb-1 lg:mx-0 lg:flex-wrap lg:px-0" role="group" aria-label={t('entries.title')}>
          {[null, ...seenForms].map((id) => {
            const on = formFilter === id;
            return (
              <button
                key={id ?? 'all'}
                type="button"
                aria-pressed={on}
                onClick={() => setFormFilter(id)}
                className={`h-[40px] shrink-0 whitespace-nowrap rounded-ui-pill px-4 text-[15px] ${on ? 'bg-ink font-bold text-bg' : 'border-[1.5px] border-border bg-panel font-medium text-ink'}`}
              >
                {id ? <bdi>{nameOf(id)}</bdi> : t('entries.allForms')}
              </button>
            );
          })}
        </div>
      )}

      {error && <p className="text-[14px] text-danger">{error}</p>}
      {items === null ? (
        <LoadingSpinner />
      ) : items.length === 0 && !error ? (
        <EmptyState title={t('entries.empty')} />
      ) : (
        groups.map(([day, list]) => (
          <section key={day} className="flex flex-col gap-2">
            <h2 className="text-[14px] font-bold text-ink-soft">{dayTitle(day)}</h2>
            {list.map((item) => {
              const def = defs[item.formId];
              return (
                <Link
                  key={item.id}
                  to={`/entries/${item.formId}/${item.id}`}
                  className="flex flex-col gap-2 rounded-[18px] border border-border bg-panel px-4 py-[14px] text-ink hover:border-ink-soft"
                >
                  <span className="flex items-baseline justify-between gap-2">
                    <span className="text-[17px] font-bold">
                      <bdi>{nameOf(item.formId)}</bdi>
                    </span>
                    <span className="whitespace-nowrap text-[14px] text-ink-soft">{sentTime(item, locale)}</span>
                  </span>
                  {def && <span className="text-[15px] text-ink-soft"><bdi>{entrySummary(def, item, ctx)}</bdi></span>}
                  <span className="flex flex-wrap gap-1.5">
                    {entryTags(item, t).map((tag) => (
                      <span key={tag.label} className={`rounded-ui-pill px-[10px] py-1 text-[13px] font-bold ${PILL[tag.tone]}`}>
                        {tag.label}
                      </span>
                    ))}
                  </span>
                </Link>
              );
            })}
          </section>
        ))
      )}
      <div ref={sentinel} />
      {next && !loading && (
        <Button variant="secondary" size="desktop" onClick={() => void load(false)} className="self-center">
          {t('entries.loadMore')}
        </Button>
      )}
      {loading && items !== null && <LoadingSpinner size="sm" />}
    </div>
  );
}
