import { useEffect, useMemo, useState } from 'react';
import { I18nextProvider, useTranslation } from 'react-i18next';
import type { FormDefinitionDto } from '../../types';
import { FormRenderer } from '../FormRenderer';

const FRAME = { width: 280, height: 560, border: 8 };
const PHONE_WIDTH = 390;

/**
 * Pane 3 of the builder (screen 21): a 280x560 phone frame (8px --ink border, radius 32) showing
 * the selected field's step through the real form renderer in preview mode, the selected field
 * outlined with a dashed --warning frame. The form is drawn at a real phone's 390px width and
 * scaled down to fit, so it looks exactly like the phone. عربي / English under it switches only
 * the preview's language (its own i18next instance - the rest of the page stays as it is).
 */
export function PhonePreview({
  token,
  form,
  stepId,
  highlightFieldId,
}: {
  token: string;
  form: FormDefinitionDto;
  stepId: string | null;
  highlightFieldId: string | null;
}) {
  const { t, i18n } = useTranslation();
  const [lang, setLang] = useState<'ar' | 'en'>(i18n.language.startsWith('ar') ? 'ar' : 'en');
  const previewI18n = useMemo(() => i18n.cloneInstance({ lng: lang }), [i18n, lang]);
  useEffect(() => {
    void previewI18n.changeLanguage(lang);
  }, [previewI18n, lang]);

  const inner = FRAME.width - FRAME.border * 2;
  const scale = inner / PHONE_WIDTH;

  return (
    <section aria-label={t('builderV2.livePreview')} className="flex max-w-[300px] flex-[1_1_280px] flex-col items-center gap-2 max-[1279px]:max-w-none max-[1279px]:basis-full">
      <span className="text-[13px] font-bold text-ink-soft">{t('builderV2.livePreview')}</span>
      <div
        dir="ltr"
        className="overflow-hidden rounded-[32px] border-[8px] border-ink bg-bg"
        style={{ width: FRAME.width, height: FRAME.height }}
        aria-hidden="true"
        // The preview is a picture of the form, not a second form to fill in.
        {...{ inert: '' }}
      >
        <div dir={lang === 'ar' ? 'rtl' : 'ltr'} lang={lang} style={{ width: PHONE_WIDTH, height: (FRAME.height - FRAME.border * 2) / scale, transform: `scale(${scale})`, transformOrigin: 'top left' }} className="overflow-y-auto" data-preview-scroll="">
          <I18nextProvider i18n={previewI18n}>
            <FormRenderer token={token} formDefinition={form} preview={{ stepId, highlightFieldId }} />
          </I18nextProvider>
        </div>
      </div>
      <div className="flex gap-1.5" role="group" aria-label={t('builderV2.previewLanguage')}>
        {(['ar', 'en'] as const).map((l) => (
          <button
            key={l}
            type="button"
            aria-pressed={lang === l}
            onClick={() => setLang(l)}
            className={`h-[32px] rounded-[8px] border px-[10px] text-[13px] ${lang === l ? 'border-accent bg-accent-soft font-bold text-accent-strong' : 'border-border bg-panel text-ink'}`}
          >
            <span lang={l} dir={l === 'ar' ? 'rtl' : 'ltr'}>{l === 'ar' ? t('builderV2.previewAr') : t('builderV2.previewEn')}</span>
          </button>
        ))}
      </div>
    </section>
  );
}
