import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { FULL_SCREEN_ATTRIBUTE } from './shell/fullScreen';
import { Icon } from './icons';
import { useShell } from './shell/ShellData';
import { Button, Card } from './ui';

/**
 * Sent (screen 07): shown in place of the form right after a successful submission. A success
 * badge, "Submitted successfully" with the time, a summary (form, project if the form has one,
 * attachments), "You've done everything due today" when nothing else is due, and Back to Today /
 * Submit another. A partly failed attachment upload keeps the existing message, as a warning
 * box in place of the success note. Takes the whole phone screen (no tab bar).
 */
export function SentScreen({
  formName,
  submittedAt,
  project,
  attachments,
  warning,
  onAnother,
}: {
  formName: string;
  submittedAt: Date;
  /** The chosen project: its field's label and the chosen value's name. */
  project: { label: string; value: string } | null;
  attachments: number;
  warning: string | null;
  onAnother: () => void;
}) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { mine, refresh } = useShell();
  const ref = useRef<HTMLDivElement>(null);
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  const time = new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit' }).format(submittedAt);

  useEffect(() => {
    refresh(); // the assignment just filled should no longer count as due
    window.scrollTo(0, 0);
    ref.current?.focus();
    document.documentElement.setAttribute(FULL_SCREEN_ATTRIBUTE, '');
    return () => document.documentElement.removeAttribute(FULL_SCREEN_ATTRIBUTE);
  }, [refresh]);

  const todays = mine ? mine.days.filter((d) => d.date === mine.today) : [];
  const allDone = todays.length > 0 && todays.every((d) => d.status !== 'pending');

  const row = (label: string, value: string) => (
    <div className="flex justify-between gap-3 text-[15px]">
      <span className="text-ink-soft">{label}</span>
      <span className="text-end font-bold text-ink">
        <bdi>{value}</bdi>
      </span>
    </div>
  );

  return (
    <div
      ref={ref}
      role="status"
      tabIndex={-1}
      className="mx-auto flex min-h-[calc(100dvh-40px)] max-w-[560px] flex-col pb-7 pt-6 outline-none lg:min-h-0"
      style={{ paddingBottom: 'calc(28px + env(safe-area-inset-bottom))' }}
    >
      <div className="flex flex-1 flex-col items-center justify-center gap-[18px] text-center">
        <span aria-hidden="true" className="flex h-24 w-24 items-center justify-center rounded-full bg-success-soft">
          <span className="flex h-16 w-16 items-center justify-center rounded-full bg-success text-accent-ink">
            <Icon name="check" size={32} strokeWidth={2.6} />
          </span>
        </span>
        <div className="flex flex-col gap-1.5">
          <h1 className="text-[26px] font-extrabold text-ink">{t('formRenderer.submittedTitle')}</h1>
          <p className="text-[16px] text-ink-soft">{t('formRenderer.submittedBody', { time })}</p>
        </div>
        <Card kind="phone-secondary" className="flex w-full flex-col gap-[10px] p-4 text-start">
          {row(t('sent.form'), formName)}
          {project && row(project.label, project.value)}
          {row(t('sent.attachments'), t('sent.photos', { count: attachments }))}
        </Card>
        {warning ? (
          <p className="flex w-full items-start gap-3 rounded-ui-2xl bg-warning-soft px-4 py-[14px] text-start text-[15px] font-medium text-warning-strong">
            <Icon name="alert" size={22} className="mt-px" />
            <span>{warning}</span>
          </p>
        ) : (
          allDone && (
            <p className="flex w-full items-center gap-3 rounded-ui-2xl bg-success-soft px-4 py-[14px] text-start text-[15px] font-medium text-success-strong">
              <Icon name="home" size={22} />
              <span>{t('today.allDone')}</span>
            </p>
          )
        )}
      </div>
      <div className="mt-6 flex flex-col gap-[10px]">
        <Button block onClick={() => navigate('/')}>
          {t('sent.backToToday')}
        </Button>
        <Button block variant="secondary" className="!min-h-[52px] !text-[17px]" onClick={onAnother}>
          {t('formRenderer.submitAnother')}
        </Button>
      </div>
    </div>
  );
}
