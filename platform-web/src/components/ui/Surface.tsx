import type { HTMLAttributes, KeyboardEvent, ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from '../icons';
import { LoadingSpinner } from '../LoadingSpinner';
import { Button } from './Buttons';

/** Card (HANDOFF §6): --panel, 1px --border, radius 20 (phone main) / 18 (phone secondary) / 16 (desktop). */
export function Card({
  kind = 'phone',
  className = '',
  children,
  ...rest
}: { kind?: 'phone' | 'phone-secondary' | 'desktop' } & HTMLAttributes<HTMLDivElement>) {
  const radius = kind === 'phone' ? 'rounded-ui-4xl' : kind === 'phone-secondary' ? 'rounded-ui-3xl' : 'rounded-ui-2xl';
  return (
    <div className={`border border-border bg-panel ${radius} ${className}`} {...rest}>
      {children}
    </div>
  );
}

/** 14px/700 --ink-soft heading above a card (09, 08 day headings). */
export function SectionHeading({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <h2 className={`text-[14px] font-bold text-ink-soft ${className}`}>{children}</h2>;
}

export type Tone = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

const toneClass: Record<Tone, string> = {
  success: 'bg-success-soft text-success',
  warning: 'bg-warning-soft text-warning',
  danger: 'bg-danger-soft text-danger',
  info: 'bg-accent-soft text-accent-strong',
  neutral: 'bg-divider text-ink-soft',
};

/** Status pill: 12-13px/700, pill radius. */
export function Pill({ tone = 'neutral', children, className = '', large = false }: { tone?: Tone; children: ReactNode; className?: string; large?: boolean }) {
  return (
    <span
      className={`inline-flex items-center gap-1 whitespace-nowrap rounded-ui-pill font-bold ${
        large ? 'px-[10px] py-[5px] text-[13px]' : 'px-[9px] py-[3px] text-[12px]'
      } ${toneClass[tone]} ${className}`}
    >
      {children}
    </span>
  );
}

/** Info banner (21) and its warning variant (22). */
export function InfoBanner({ tone = 'info', children, className = '' }: { tone?: 'info' | 'warning'; children: ReactNode; className?: string }) {
  return (
    <div
      className={`rounded-ui-lg px-[14px] py-[10px] text-[14px] ${
        tone === 'info' ? 'bg-accent-soft text-accent-strong' : 'bg-warning-soft text-warning-strong'
      } ${className}`}
    >
      {children}
    </div>
  );
}

/** Empty state: dashed box, a 14px/700 title, and a line saying what will fill it. */
export function EmptyState({ title, body, className = '' }: { title: ReactNode; body?: ReactNode; className?: string }) {
  return (
    <div className={`flex flex-col items-center justify-center gap-1 rounded-ui-lg border-[1.5px] border-dashed border-border px-4 py-8 text-center ${className}`}>
      <p className="text-[14px] font-bold text-ink">{title}</p>
      {body && <p className="text-[13px] text-ink-soft">{body}</p>}
    </div>
  );
}

// ------------------------------------------------------------------ day cells (02, 23)

export type DayState = 'filled' | 'missed' | 'due' | 'off';

/**
 * A day in the "last 2 weeks" strip and the assignments grid: filled = --success, not filled =
 * --missed, due today = dashed --warning outline, Friday / outside the assignment = --off.
 * Always has a text name (date + state) for screen readers and a hover title.
 */
export function DayCell({
  state,
  label,
  size = 40,
  height,
  radius = 7,
  onClick,
}: {
  state: DayState;
  label: string;
  /** A number of px, or 'fill' to take the column's width (the 12-day strip on 02). */
  size?: number | 'fill';
  height?: number;
  radius?: number;
  onClick?: () => void;
}) {
  const style =
    state === 'filled'
      ? 'bg-success'
      : state === 'missed'
        ? 'bg-missed'
        : state === 'due'
          ? 'border-[1.5px] border-dashed border-warning bg-transparent'
          : 'bg-off';
  const common = `block shrink-0 ${style}`;
  const dims = { width: size === 'fill' ? '100%' : size, height: height ?? (size === 'fill' ? 26 : size), borderRadius: radius };
  return onClick ? (
    <button type="button" title={label} aria-label={label} onClick={onClick} className={`${common} hover:brightness-110`} style={dims} />
  ) : (
    <span role="img" title={label} aria-label={label} className={common} style={dims} />
  );
}

/** The legend under every day strip / grid. */
export function DayLegend({ showOff = false }: { showOff?: boolean }) {
  const { t } = useTranslation();
  const item = (state: DayState, text: string) => (
    <span className="inline-flex items-center gap-1.5">
      <DayCell state={state} label={text} size={10} radius={3} />
      <span>{text}</span>
    </span>
  );
  return (
    <div className="flex flex-wrap items-center gap-x-[14px] gap-y-1 text-[13px] text-ink-soft" aria-hidden="true">
      {item('filled', t('assignments.status.filled'))}
      {item('missed', t('assignments.status.missed'))}
      {item('due', t('today.dueToday'))}
      {showOff && item('off', t('assignmentsV2.legendOff'))}
    </div>
  );
}

// ------------------------------------------------------------------ tables (desktop)

/** Table inside a card, scrolling sideways on its own when it must. */
export function TableCard({ children, footer, className = '' }: { children: ReactNode; footer?: ReactNode; className?: string }) {
  return (
    <Card kind="desktop" className={`min-w-0 overflow-hidden ${className}`}>
      <div className="overflow-x-auto">
        <table className="w-full border-collapse text-[14px]">{children}</table>
      </div>
      {footer}
    </Card>
  );
}

export const thClass =
  'whitespace-nowrap bg-table-head px-[10px] py-3 text-start text-[14px] font-bold text-ink-soft first:ps-[18px] last:pe-[18px]';
export const tdClass = 'border-t border-divider px-[10px] py-3 align-middle text-ink first:ps-[18px] last:pe-[18px]';

/** A whole-row-clickable, keyboard-focusable table row; selected = --accent-row + 3px bar. */
export function ClickableRow({
  selected,
  onOpen,
  children,
  label,
}: {
  selected?: boolean;
  onOpen: () => void;
  children: ReactNode;
  label?: string;
}) {
  return (
    <tr
      tabIndex={0}
      aria-selected={selected}
      aria-label={label}
      onClick={onOpen}
      onKeyDown={(e: KeyboardEvent<HTMLTableRowElement>) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onOpen();
        }
      }}
      className={`cursor-pointer hover:bg-accent-row focus-visible:bg-accent-row ${
        selected ? 'bg-accent-row [&>td:first-child]:shadow-[inset_-3px_0_0_var(--accent)] [&>td:first-child]:ltr:shadow-[inset_3px_0_0_var(--accent)]' : ''
      }`}
    >
      {children}
    </tr>
  );
}

/** "1-9 of 46" on the start side, Previous/Next on the end side. */
export function Pager({
  from,
  to,
  total,
  onPrevious,
  onNext,
}: {
  from: number;
  to: number;
  total: number;
  onPrevious?: () => void;
  onNext?: () => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="flex items-center justify-between gap-3 border-t border-divider px-[18px] py-3 text-[14px] text-ink-soft">
      <span>{t('submissionsTable.showing', { from: total === 0 ? 0 : from, to, total })}</span>
      <div className="flex gap-2">
        <Button variant="secondary" size="compact" className="!border" disabled={!onPrevious} onClick={onPrevious}>
          {t('submissionsTable.previous')}
        </Button>
        <Button variant="secondary" size="compact" className="!border" disabled={!onNext} onClick={onNext}>
          {t('submissionsTable.next')}
        </Button>
      </div>
    </div>
  );
}

// ------------------------------------------------------------------ loading / load failed (phase 10)

/** A page or panel still loading: the animated mark, centred, announced as "Loading…". */
export function LoadingState({ size = 'md', className = '' }: { size?: 'sm' | 'md' | 'lg'; className?: string }) {
  const { t } = useTranslation();
  return (
    <div role="status" className={`flex items-center justify-center py-10 ${className}`}>
      <LoadingSpinner size={size} />
      <span className="sr-only">{t('common.loading')}</span>
    </div>
  );
}

/**
 * Something didn't load: the empty-state box with an alert mark, what went wrong, and
 * "Try again" (when the page can retry). Used in place of a bare red line so every screen
 * fails the same way and can recover without a reload.
 */
export function ErrorState({ message, onRetry, className = '' }: { message: ReactNode; onRetry?: () => void; className?: string }) {
  const { t } = useTranslation();
  return (
    <div role="alert" className={`flex flex-col items-center justify-center gap-3 rounded-ui-lg border-[1.5px] border-dashed border-border px-4 py-8 text-center ${className}`}>
      <span aria-hidden="true" className="flex h-10 w-10 items-center justify-center rounded-full bg-danger-soft text-danger">
        <Icon name="alert" size={20} />
      </span>
      <p className="max-w-[420px] text-[14px] font-bold text-ink">{message}</p>
      {onRetry && (
        <Button size="compact" variant="secondary" onClick={onRetry}>
          {t('common.tryAgain')}
        </Button>
      )}
    </div>
  );
}
