import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import type { DropdownOption, FieldDefinitionDto, FormStepDto } from '../types';

/**
 * Bilingual labels (redesign HANDOFF §9.1): every form name, field label, option label and step
 * title has an Arabic and an English version. Show the one for the UI language, falling back to
 * the other when it is empty - never blank. (Fixed UI words come from i18n, not from here.)
 */
export function pickLanguage(ar: string | null | undefined, en: string | null | undefined, language: string): string {
  const arabic = (ar ?? '').trim();
  const english = (en ?? '').trim();
  return language.startsWith('ar') ? arabic || english : english || arabic;
}

/** The current language's version of each kind of label; re-renders on a language switch. */
export function useLocalized() {
  const { i18n } = useTranslation();
  const lang = i18n.language;
  const formName = useCallback((f: { name: string; nameEn?: string | null }) => pickLanguage(f.name, f.nameEn, lang), [lang]);
  const fieldLabel = useCallback((f: Pick<FieldDefinitionDto, 'label' | 'labelEn'>) => pickLanguage(f.label, f.labelEn, lang), [lang]);
  const optionLabel = useCallback((o: DropdownOption) => pickLanguage(o.label, o.labelEn, lang), [lang]);
  const stepTitle = useCallback((s: FormStepDto) => pickLanguage(s.titleAr, s.titleEn, lang), [lang]);
  return { lang, formName, fieldLabel, optionLabel, stepTitle };
}
