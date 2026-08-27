interface SwitchProps {
  checked: boolean;
  onChange: () => void;
  leftLabel: string;
  rightLabel: string;
  /** 'dark' for the sidebar surface, 'light' for a panel/page background (e.g. the
   *  sign-in card) - same meaning as it had on LanguageToggle before this became the
   *  shared component both toggles are built from. */
  tone?: 'dark' | 'light';
  ariaLabel: string;
}

const TONE_CLASSES = {
  dark: { active: 'font-medium text-sidebar-ink-strong', inactive: 'text-sidebar-ink' },
  light: { active: 'font-medium text-ink', inactive: 'text-ink-soft' },
};

/**
 * Shared iOS-style sliding switch - LanguageToggle and ModeToggle are both thin
 * wrappers around this, not two separate implementations. Track fill reflects state
 * (bg-accent when checked/right-hand position is active, bg-border otherwise) rather
 * than a flat translucent color regardless of state.
 *
 * dir="ltr" is load-bearing, not decoration: with no rtl:/ltr: classes anywhere in
 * here, this element would otherwise inherit dir="rtl" from <html> once Arabic is
 * active, and the browser's own default flex-direction:row is direction-aware
 * independent of Tailwind - it silently reverses child paint order under an inherited
 * RTL context. A control like this needs to look and behave the same way regardless
 * of which language is currently active, or it stops being findable/usable. This
 * fixes the button's OWN internal layout - callers rendering more than one Switch
 * side by side still need dir="ltr" on whatever wraps them together too, or the pair
 * reorders relative to each other even though each one individually stays correct.
 */
export function Switch({ checked, onChange, leftLabel, rightLabel, tone = 'dark', ariaLabel }: SwitchProps) {
  const classes = TONE_CLASSES[tone];

  return (
    <button
      onClick={onChange}
      dir="ltr"
      className="flex items-center gap-1.5 text-xs uppercase tracking-wide"
      aria-label={ariaLabel}
    >
      <span className={checked ? classes.inactive : classes.active}>{leftLabel}</span>
      <span
        className={`relative inline-flex h-[22px] w-11 shrink-0 items-center rounded-full transition-colors duration-200 ${
          checked ? 'bg-accent' : 'bg-border'
        }`}
      >
        {/* translate-x-[22px], not Tailwind's translate-x-5 (20px): the knob rests at
            left-0.5 (2px), and needs to travel exactly track_width - knob_width - 2*inset
            = 44 - 18 - 2*2 = 22px to land with the same 2px inset flush against the
            right edge that it starts with on the left - 20px left it 2px short,
            landing at a 4px right gap instead of 2px. Measured via getBoundingClientRect
            in both states to confirm, not eyeballed. */}
        <span
          className={`absolute left-0.5 h-[18px] w-[18px] rounded-full bg-white shadow transition-transform duration-200 ${
            checked ? 'translate-x-[22px]' : 'translate-x-0'
          }`}
        />
      </span>
      <span className={checked ? classes.active : classes.inactive}>{rightLabel}</span>
    </button>
  );
}
