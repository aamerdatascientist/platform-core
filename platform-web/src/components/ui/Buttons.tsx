import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Icon } from '../icons';

/**
 * Redesign buttons (HANDOFF §6). Sizes follow the shell: `size="phone"` is the 56px thumb-zone
 * button, `size="desktop"` the 42px one. Destructive actions are never the filled primary.
 */
type Variant = 'primary' | 'secondary' | 'danger' | 'success' | 'link';
type Size = 'phone' | 'desktop' | 'compact';

const base =
  'inline-flex items-center justify-center gap-2 font-sans transition-colors disabled:cursor-not-allowed disabled:opacity-50';

const sizes: Record<Size, string> = {
  phone: 'min-h-[56px] rounded-r-2xl px-5 text-[18px] font-bold',
  desktop: 'min-h-[42px] rounded-r-lg px-4 text-[15px] font-bold',
  compact: 'min-h-[44px] rounded-r-md px-3 text-[14px] font-medium',
};

const variants: Record<Variant, string> = {
  primary: 'bg-accent text-accent-ink hover:brightness-110',
  success: 'bg-success text-accent-ink hover:brightness-110',
  secondary: 'border-[1.5px] border-border bg-panel text-ink !font-medium hover:border-ink-soft',
  danger: 'border-[1.5px] border-danger-border bg-panel text-danger !font-medium hover:bg-danger-soft',
  link: 'min-h-[44px] bg-transparent px-2 text-accent hover:text-accent-strong !font-medium',
};

export function Button({
  variant = 'primary',
  size = 'phone',
  block = false,
  icon,
  iconEnd,
  className = '',
  children,
  type = 'button',
  ...rest
}: {
  variant?: Variant;
  size?: Size;
  block?: boolean;
  icon?: ReactNode;
  iconEnd?: ReactNode;
} & ButtonHTMLAttributes<HTMLButtonElement>) {
  const desktopBorder = size !== 'phone' && (variant === 'secondary' || variant === 'danger') ? '!border' : '';
  return (
    <button
      type={type}
      className={`${base} ${variant === 'link' ? '' : sizes[size]} ${variants[variant]} ${desktopBorder} ${block ? 'w-full' : ''} ${className}`}
      {...rest}
    >
      {icon}
      {children}
      {iconEnd}
    </button>
  );
}

/** A 44x44 (or larger) icon-only button; the label is required (screen readers, tooltips). */
export function IconButton({
  label,
  children,
  className = '',
  size = 44,
  round = false,
  ...rest
}: { label: string; size?: number; round?: boolean } & ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={`inline-flex shrink-0 items-center justify-center text-ink hover:bg-field-muted ${round ? 'rounded-full' : 'rounded-r-lg'} ${className}`}
      style={{ width: size, height: size }}
      {...rest}
    >
      {children}
    </button>
  );
}

// ------------------------------------------------------------------ choice controls

export interface ChoiceOption<T extends string> {
  value: T;
  label: string;
}

const choiceOn = 'border-accent bg-accent-soft text-accent-strong font-bold';
const choiceOff = 'border-border bg-panel text-ink font-medium hover:border-ink-soft';

/**
 * Single-select chips (03 weather, 05 phase): wrapping row, 46px pills, a check on the chosen
 * one. Radio semantics; arrow keys move between chips.
 */
export function ChipGroup<T extends string>({
  options,
  value,
  onChange,
  labelledBy,
  invalid,
  describedBy,
  compact = false,
}: {
  options: ChoiceOption<T>[];
  value: T | null;
  onChange: (value: T) => void;
  labelledBy?: string;
  invalid?: boolean;
  describedBy?: string;
  compact?: boolean;
}) {
  return (
    <div
      role="radiogroup"
      aria-labelledby={labelledBy}
      aria-invalid={invalid || undefined}
      aria-describedby={describedBy}
      className="flex flex-wrap gap-2"
      onKeyDown={(e) => moveFocus(e, options.findIndex((o) => o.value === value), options.length, (i) => onChange(options[i].value))}
    >
      {options.map((option) => {
        const on = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={on}
            tabIndex={on || (value === null && option === options[0]) ? 0 : -1}
            onClick={() => onChange(option.value)}
            className={`inline-flex h-[46px] items-center gap-1.5 rounded-r-pill border-[1.5px] text-[16px] ${compact ? 'px-4' : 'px-[18px]'} ${on ? choiceOn : choiceOff} ${invalid && !on ? '!border-danger' : ''}`}
          >
            {on && <Icon name="check" size={16} strokeWidth={2.6} />}
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

/** Yes/No (05): two equal 52px buttons, Yes first (on the right in Arabic). */
export function YesNo({
  value,
  onChange,
  yesLabel,
  noLabel,
  labelledBy,
  invalid,
  describedBy,
}: {
  value: boolean | null;
  onChange: (value: boolean) => void;
  yesLabel: string;
  noLabel: string;
  labelledBy?: string;
  invalid?: boolean;
  describedBy?: string;
}) {
  const options: [boolean, string][] = [
    [true, yesLabel],
    [false, noLabel],
  ];
  return (
    <div
      role="radiogroup"
      aria-labelledby={labelledBy}
      aria-invalid={invalid || undefined}
      aria-describedby={describedBy}
      className="grid grid-cols-2 gap-2"
      onKeyDown={(e) => moveFocus(e, value === null ? -1 : value ? 0 : 1, 2, (i) => onChange(i === 0))}
    >
      {options.map(([v, label]) => {
        const on = value === v;
        return (
          <button
            key={String(v)}
            type="button"
            role="radio"
            aria-checked={on}
            tabIndex={on || (value === null && v) ? 0 : -1}
            onClick={() => onChange(v)}
            className={`h-[52px] rounded-r-xl border-[1.5px] text-[17px] ${on ? choiceOn : choiceOff} ${invalid && !on ? '!border-danger' : ''}`}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
}

/**
 * Segmented control (09 language/appearance, 22 filter, 24 report kind): a muted track, the
 * chosen segment raised on --panel with --elev-1.
 */
export function Segmented<T extends string>({
  options,
  value,
  onChange,
  label,
  size = 'phone',
}: {
  options: ChoiceOption<T>[];
  value: T;
  onChange: (value: T) => void;
  label: string;
  size?: 'phone' | 'desktop';
}) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="grid gap-1 rounded-r-lg bg-field-muted p-1"
      style={{ gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` }}
      onKeyDown={(e) => moveFocus(e, options.findIndex((o) => o.value === value), options.length, (i) => onChange(options[i].value))}
    >
      {options.map((option) => {
        const on = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={on}
            tabIndex={on ? 0 : -1}
            onClick={() => onChange(option.value)}
            className={`${size === 'phone' ? 'h-[42px] text-[16px]' : 'h-[36px] text-[14px]'} truncate rounded-r-md px-2 ${
              on ? 'bg-panel font-bold text-ink shadow-elev-1' : 'font-medium text-ink-soft hover:text-ink'
            }`}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

/** Arrow keys inside a radio group: move and select, in the reading direction. */
function moveFocus(e: React.KeyboardEvent<HTMLElement>, current: number, count: number, select: (index: number) => void) {
  const group = e.currentTarget;
  const rtl = getComputedStyle(group).direction === 'rtl';
  const forward = ['ArrowDown', rtl ? 'ArrowLeft' : 'ArrowRight'];
  const back = ['ArrowUp', rtl ? 'ArrowRight' : 'ArrowLeft'];
  if (![...forward, ...back].includes(e.key)) return;
  e.preventDefault();
  const next = forward.includes(e.key) ? (current + 1 + count) % count : (current - 1 + count * 2) % count;
  select(next);
  requestAnimationFrame(() => {
    group.querySelectorAll<HTMLElement>('[role="radio"]')[next]?.focus();
  });
}
