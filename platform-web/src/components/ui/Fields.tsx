import { useId, useState } from 'react';
import type { InputHTMLAttributes, ReactNode, TextareaHTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';
import { CalendarPanel, formatLongDate, todayIso, yesterdayIso } from '../DateField';
import { Icon } from '../icons';
import { useIsDesktop } from './useMedia';

/**
 * Label + control + hint + inline error (HANDOFF §6): 15px/700 label, 8px gap, " *" in
 * --danger for required, 13px hint, 14px --danger error line with an alert icon. The render
 * prop hands the control its ids so aria-invalid/aria-describedby are always wired.
 */
export function FieldShell({
  label,
  required,
  hint,
  error,
  asGroup = false,
  children,
}: {
  label: ReactNode;
  required?: boolean;
  hint?: ReactNode;
  error?: string | null;
  /** Chips, yes/no, date buttons: a group of buttons labelled by the label (fieldset/legend). */
  asGroup?: boolean;
  children: (ids: { inputId: string; labelId: string; describedBy?: string; invalid: boolean }) => ReactNode;
}) {
  const id = useId();
  const inputId = `${id}-input`;
  const labelId = `${id}-label`;
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [errorId, hintId].filter(Boolean).join(' ') || undefined;
  const labelContent = (
    <>
      {label}
      {required && <span className="text-danger"> *</span>}
    </>
  );
  const body = (
    <>
      {children({ inputId, labelId, describedBy, invalid: !!error })}
      {error && (
        <p id={errorId} className="flex items-start gap-1.5 text-[14px] text-danger">
          <Icon name="alert" size={16} className="mt-0.5" />
          <span>{error}</span>
        </p>
      )}
      {hint && (
        <p id={hintId} className="text-[13px] text-ink-soft">
          {hint}
        </p>
      )}
    </>
  );

  return asGroup ? (
    <fieldset className="m-0 flex min-w-0 flex-col gap-2 border-0 p-0">
      <legend id={labelId} className="mb-2 p-0 text-[15px] font-bold text-ink">
        {labelContent}
      </legend>
      {body}
    </fieldset>
  ) : (
    <div className="flex min-w-0 flex-col gap-2">
      <label id={labelId} htmlFor={inputId} className="text-[15px] font-bold text-ink">
        {labelContent}
      </label>
      {body}
    </div>
  );
}

/** 56px on a phone / 44px on a desktop; 17px text on a phone (no iOS zoom). */
export function inputClass(invalid?: boolean, desktop?: boolean) {
  return `w-full min-w-0 border-[1.5px] bg-panel px-[14px] text-ink placeholder:text-ink-soft focus:border-accent focus:shadow-focus-ring focus:outline-none ${
    desktop ? 'h-[44px] rounded-r-md text-[15px]' : 'h-[56px] rounded-r-xl text-[17px]'
  } ${invalid ? '!border-danger !bg-danger-soft' : 'border-border'}`;
}

export function TextInput({ invalid, className = '', ...rest }: { invalid?: boolean } & InputHTMLAttributes<HTMLInputElement>) {
  const desktop = useIsDesktop();
  return <input aria-invalid={invalid || undefined} className={`${inputClass(invalid, desktop)} ${className}`} {...rest} />;
}

/** Long text: 3 rows, radius 14, no resize handle. */
export function TextArea({ invalid, className = '', ...rest }: { invalid?: boolean } & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      rows={3}
      aria-invalid={invalid || undefined}
      className={`w-full min-w-0 resize-none rounded-r-xl border-[1.5px] bg-panel px-[14px] py-3 text-[17px] text-ink placeholder:text-ink-soft focus:border-accent focus:shadow-focus-ring focus:outline-none md:text-[15px] ${
        invalid ? '!border-danger !bg-danger-soft' : 'border-border'
      } ${className}`}
      {...rest}
    />
  );
}

/**
 * Whole-number stepper (03, 11): 56x56 buttons either side of the value, "+" always on the right
 * and "-" on the left in both languages (as drawn on 03 and 11), so the row is pinned LTR. The value stays a string so a half-typed or Arabic-digit
 * entry is validated by the existing field rules, not silently rewritten.
 */
export function Stepper({
  id,
  value,
  onChange,
  invalid,
  describedBy,
  min,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  invalid?: boolean;
  describedBy?: string;
  min?: number;
}) {
  const { t } = useTranslation();
  const current = Number.parseInt(value, 10);
  const step = (delta: number) => {
    const base = Number.isFinite(current) ? current : 0;
    const next = base + delta;
    if (min !== undefined && next < min) return;
    onChange(String(next));
  };
  const square =
    'flex h-[56px] w-[56px] shrink-0 items-center justify-center rounded-r-xl border-[1.5px] border-border bg-panel text-ink hover:border-ink-soft';
  return (
    <div dir="ltr" className="flex items-center gap-[10px]">
      <button type="button" className={square} aria-label={t('steps.decrease')} onClick={() => step(-1)}>
        <Icon name="minus" size={22} />
      </button>
      <input
        id={id}
        type="text"
        inputMode="numeric"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        className={`h-[56px] min-w-0 flex-1 rounded-r-xl border-[1.5px] bg-panel text-center text-[24px] font-bold text-ink focus:border-accent focus:shadow-focus-ring focus:outline-none ${
          invalid ? '!border-danger !bg-danger-soft' : 'border-border'
        }`}
      />
      <button type="button" className={square} aria-label={t('steps.increase')} onClick={() => step(1)}>
        <Icon name="plus" size={22} />
      </button>
    </div>
  );
}

/**
 * Date quick pick (03): Today / Yesterday / Other date, 48px each; "Other date" opens the
 * existing calendar. Under it, the chosen date written out in the UI language.
 */
export function DateQuickPick({
  value,
  onChange,
  maxDate,
  invalid,
}: {
  value: string;
  onChange: (value: string) => void;
  maxDate?: string;
  invalid?: boolean;
}) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const today = todayIso();
  const yesterday = yesterdayIso();
  const other = !!value && value !== today && value !== yesterday;
  const cls = (on: boolean) =>
    `flex h-[48px] items-center justify-center gap-1.5 rounded-r-lg border-[1.5px] text-[16px] ${
      on ? 'border-accent bg-accent-soft font-bold text-accent-strong' : `bg-panel font-medium text-ink hover:border-ink-soft ${invalid ? 'border-danger' : 'border-border'}`
    }`;
  return (
    <div className="flex flex-col gap-2">
      <div className="grid grid-cols-3 gap-2">
        <button type="button" aria-pressed={value === today} className={cls(value === today)} onClick={() => { onChange(today); setOpen(false); }}>
          {t('formRenderer.today')}
        </button>
        <button type="button" aria-pressed={value === yesterday} className={cls(value === yesterday)} onClick={() => { onChange(yesterday); setOpen(false); }}>
          {t('formRenderer.yesterday')}
        </button>
        <button type="button" aria-pressed={other} aria-expanded={open} className={cls(other)} onClick={() => setOpen((o) => !o)}>
          <Icon name="calendar" size={16} />
          {t('steps.otherDate')}
        </button>
      </div>
      {open && (
        <CalendarPanel
          value={value}
          maxDate={maxDate}
          onPick={(iso) => {
            onChange(iso);
            setOpen(false);
          }}
        />
      )}
      {value && <span className="text-[13px] text-ink-soft">{formatLongDate(value, i18n.language)}</span>}
    </div>
  );
}

/**
 * Lookup row (03, 05): looks like a field, opens the bottom sheet (phone) or dialog (desktop).
 * Disabled with "Choose {{label}} first" when it depends on an empty field.
 */
export function LookupRow({
  id,
  value,
  sub,
  placeholder,
  onOpen,
  disabled,
  invalid,
  describedBy,
  icon = 'pin',
}: {
  id: string;
  value: string | null;
  sub?: string | null;
  placeholder: string;
  onOpen: () => void;
  disabled?: boolean;
  invalid?: boolean;
  describedBy?: string;
  icon?: 'pin' | 'search' | 'list' | null;
}) {
  return (
    <button
      id={id}
      type="button"
      onClick={onOpen}
      disabled={disabled}
      aria-haspopup="dialog"
      aria-invalid={invalid || undefined}
      aria-describedby={describedBy}
      className={`flex min-h-[56px] w-full items-center gap-3 rounded-r-xl border-[1.5px] px-[14px] py-2 text-start disabled:cursor-not-allowed ${
        disabled ? 'border-border bg-field-muted text-ink-soft' : invalid ? 'border-danger bg-danger-soft' : 'border-border bg-panel hover:border-ink-soft'
      }`}
    >
      {icon && !disabled && <Icon name={icon} size={20} className="text-accent" />}
      <span className="flex min-w-0 flex-1 flex-col">
        <span className={`truncate text-[17px] font-medium ${value ? 'text-ink' : 'text-ink-soft'}`}>{value ?? placeholder}</span>
        {value && sub && <span className="truncate text-[13px] text-ink-soft">{sub}</span>}
      </span>
      <Icon name="chevronDown" size={18} className="text-ink-soft" />
    </button>
  );
}
