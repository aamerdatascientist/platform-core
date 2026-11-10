import { useTranslation } from 'react-i18next';
import { localeFor, shownSections } from '../../forms/recordAnswers';
import { useLocalized } from '../../forms/localized';
import type { FormDefinitionDto, RecordItemDto } from '../../types';

/**
 * A record's repeatable sections (2026-11-10): one card per block - "Stage 1", "Stage 2" -
 * with the answers that block had, the same rows the rest of the record uses.
 */
export function RecordBlocks({ form, record, compact = false }: { form: FormDefinitionDto; record: RecordItemDto; compact?: boolean }) {
  const { t, i18n } = useTranslation();
  const { fieldLabel } = useLocalized();
  const sections = shownSections(form, record, { t, lang: i18n.language, locale: localeFor(i18n.language) });
  if (sections.length === 0) return null;
  return (
    <>
      {sections.flatMap((s) =>
        s.blocks.map((block) => (
          <section
            key={`${s.section.id}-${block.index}`}
            data-record-block={`${s.section.code}-${block.index}`}
            className={`flex flex-col border border-border bg-panel ${compact ? 'rounded-ui-xl' : 'rounded-[18px]'}`}
          >
            <h2 className="px-4 pb-2 pt-3 text-[16px] font-bold text-ink">
              <bdi>{s.title}</bdi> {block.index + 1}
            </h2>
            {block.rows.map((r) => (
              <div key={r.field.id} className="flex justify-between gap-3 border-t border-divider px-4 py-[10px]">
                <span className="shrink-0 text-[15px] text-ink-soft">
                  <bdi>{fieldLabel(r.field)}</bdi>
                </span>
                <span className="min-w-0 text-end text-[15px] font-bold text-ink">
                  <bdi>{r.value}</bdi>
                </span>
              </div>
            ))}
          </section>
        )),
      )}
    </>
  );
}
