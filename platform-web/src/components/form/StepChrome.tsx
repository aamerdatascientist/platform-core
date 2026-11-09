import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from '../icons';
import { IconButton } from '../ui';

/**
 * Step header (03, 05, 06, 11): a --panel bar with a bottom border. Close (step 1) or back
 * (later steps), the form name with "Step n of N · step title", "Draft saved" at the end when
 * a draft exists, and a progress bar of N equal 5px segments (done/current --accent).
 */
export function StepHeader({
  title,
  step,
  total,
  stepTitle,
  onClose,
  onBack,
  draftSaved,
}: {
  title: string;
  step: number;
  total: number;
  stepTitle: string;
  onClose: () => void;
  onBack?: () => void;
  draftSaved?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <header className="sticky top-0 z-20 flex flex-col gap-3 border-b border-border bg-panel px-3 pb-[14px] pt-[10px]">
      <div className="flex items-center gap-1">
        {step > 1 && onBack ? (
          <IconButton label={t('steps.back')} onClick={onBack}>
            <Icon name="arrowBack" size={22} />
          </IconButton>
        ) : (
          <IconButton label={t('steps.close')} onClick={onClose}>
            <Icon name="close" size={22} />
          </IconButton>
        )}
        <div className="flex min-w-0 flex-1 flex-col">
          <span className="truncate text-[17px] font-bold text-ink">
            <bdi>{title}</bdi>
          </span>
          <span className="truncate text-[13px] text-ink-soft">{t('steps.stepOf', { n: step, total, title: stepTitle })}</span>
        </div>
        {draftSaved && (
          <span className="flex shrink-0 items-center gap-1.5 pe-1.5 text-[13px] text-success">
            <Icon name="cloudCheck" size={16} />
            {t('steps.draftSaved')}
          </span>
        )}
      </div>
      <div className="grid gap-1.5 px-1.5" style={{ gridTemplateColumns: `repeat(${total}, minmax(0, 1fr))` }} aria-hidden="true">
        {Array.from({ length: total }, (_, i) => (
          <span key={i} className={`h-[5px] rounded-ui-pill ${i < step ? 'bg-accent' : 'bg-border'}`} />
        ))}
      </div>
    </header>
  );
}

/** Step footer: a --panel bar with a top border, 12/20/22px padding plus the safe area; stays at the bottom. */
export function StepFooter({ children }: { children: ReactNode }) {
  return (
    <footer
      className="sticky bottom-0 z-20 flex gap-2 border-t border-border bg-panel px-5 pt-3"
      style={{ paddingBottom: 'calc(22px + env(safe-area-inset-bottom))' }}
    >
      {children}
    </footer>
  );
}
