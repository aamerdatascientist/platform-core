interface StatusLedProps {
  label: string;
  tone?: 'accent' | 'success' | 'danger' | 'muted' | 'sidebar';
}

const TONE_CLASS = {
  accent: 'text-accent',
  success: 'text-success',
  danger: 'text-danger',
  muted: 'text-ink-soft',
  // For use on the sidebar surface specifically - ink-soft is tuned for panel/page
  // backgrounds and reads too low-contrast against the sidebar's own color range.
  sidebar: 'text-sidebar-ink',
};

/**
 * Glowing dot + uppercase mono label - the "Structural steel" replacement for every
 * flat status pill/badge in the app (WorkflowPanel's current-state indicator,
 * FormBuilder's Draft/Published and "editing new version" pills, BuilderHome's and
 * FormPicker's plain status text). One reusable component for all of them, not a
 * bordered-pill container - some of those call sites never had a border/pill to begin
 * with, and this is meant to work as a lightweight inline treatment either way.
 * `currentColor` in the glow means the box-shadow always matches whichever tone class
 * sets the text color - no separate color prop to keep in sync.
 */
export function StatusLed({ label, tone = 'accent' }: StatusLedProps) {
  return (
    <span className={`inline-flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-wider ${TONE_CLASS[tone]}`}>
      <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-current" style={{ boxShadow: '0 0 5px currentColor' }} />
      {label}
    </span>
  );
}
