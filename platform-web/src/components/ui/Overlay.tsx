import { useEffect, useId, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { Icon } from '../icons';
import { IconButton } from './Buttons';
import { useIsDesktop, useMediaQuery } from './useMedia';

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Keeps keyboard focus inside `ref` while open, closes on Escape, and hands focus back to
 * whatever had it before (the trigger) when it closes (HANDOFF §12, keyboard).
 */
export function useModalFocus(open: boolean, ref: React.RefObject<HTMLElement>, onClose: () => void) {
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement as HTMLElement | null;
    const node = ref.current;
    const first = node?.querySelector<HTMLElement>('[data-autofocus]') ?? node?.querySelector<HTMLElement>(FOCUSABLE);
    (first ?? node)?.focus();

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        closeRef.current();
        return;
      }
      if (e.key !== 'Tab' || !node) return;
      const items = Array.from(node.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => el.offsetParent !== null);
      if (items.length === 0) return;
      const firstItem = items[0];
      const lastItem = items[items.length - 1];
      if (e.shiftKey && document.activeElement === firstItem) {
        e.preventDefault();
        lastItem.focus();
      } else if (!e.shiftKey && document.activeElement === lastItem) {
        e.preventDefault();
        firstItem.focus();
      }
    };
    document.addEventListener('keydown', onKey, true);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey, true);
      document.body.style.overflow = overflow;
      previous?.focus?.();
    };
  }, [open, ref]);
}

/**
 * Bottom sheet on a phone (04): scrim, --panel sheet with a 24px top radius, about 76% of
 * the viewport, 40x5 handle, 20px/800 title and a close button; slides up 220ms; Esc, a scrim
 * tap or a downward swipe on the handle closes it. On a desktop the same content is a centred
 * 480px dialog.
 */
export function Sheet({
  open,
  onClose,
  title,
  children,
  footer,
  desktopWidth = 480,
}: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  desktopWidth?: number;
}) {
  const { t } = useTranslation();
  const desktop = useIsDesktop();
  const ref = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const [drag, setDrag] = useState(0);
  const start = useRef<number | null>(null);
  useModalFocus(open, ref, onClose);
  if (!open) return null;

  const header = (
    <div className="flex items-center justify-between gap-3 py-1.5 pl-5 pr-3">
      <h2 id={titleId} className="text-[20px] font-extrabold text-ink">
        {title}
      </h2>
      <IconButton label={t('steps.close')} onClick={onClose} round>
        <Icon name="close" size={22} />
      </IconButton>
    </div>
  );

  return createPortal(
    <div className={`fixed inset-0 z-50 flex ${desktop ? 'items-center justify-center p-6' : 'items-end'}`}>
      <div className="absolute inset-0 animate-[scrim-in_220ms_ease-out]" style={{ background: 'var(--scrim)' }} onClick={onClose} aria-hidden="true" />
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className={
          desktop
            ? 'relative flex max-h-[80vh] w-full flex-col rounded-ui-2xl border border-border bg-panel pt-4 focus:outline-none'
            : 'relative flex h-[76vh] w-full animate-[sheet-up_220ms_ease-out] flex-col rounded-t-ui-sheet bg-panel focus:outline-none'
        }
        style={desktop ? { maxWidth: desktopWidth } : { transform: drag ? `translateY(${drag}px)` : undefined, paddingBottom: 'env(safe-area-inset-bottom)' }}
      >
        {!desktop && (
          <div
            className="flex justify-center pb-1 pt-[10px]"
            onTouchStart={(e) => (start.current = e.touches[0].clientY)}
            onTouchMove={(e) => start.current !== null && setDrag(Math.max(0, e.touches[0].clientY - start.current))}
            onTouchEnd={() => {
              if (drag > 90) onClose();
              setDrag(0);
              start.current = null;
            }}
          >
            <span className="h-[5px] w-10 rounded-ui-pill bg-sheet-handle" aria-hidden="true" />
          </div>
        )}
        {header}
        <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-4">{children}</div>
        {footer && <div className="px-5 pb-[22px] pt-3">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}

/**
 * Desktop drawer (20, 22): a card beside the table (flex 1 1 340px, max 400px); below 1280px it
 * becomes an overlay sliding in from the inline-end side. Header 18px/800 + sub-line + close,
 * sections separated by --divider, footer actions pinned at the bottom.
 */
export function Drawer({
  open,
  onClose,
  title,
  sub,
  leading,
  children,
  footer,
}: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  sub?: ReactNode;
  leading?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const { t } = useTranslation();
  const wide = useMediaQuery('(min-width: 1280px)');
  const ref = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useModalFocus(open && !wide, ref, onClose);
  useEffect(() => {
    if (!open || !wide) return;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open, wide, onClose]);
  if (!open) return null;

  const card = (
    <aside
      ref={ref}
      role={wide ? 'complementary' : 'dialog'}
      aria-modal={wide ? undefined : true}
      aria-labelledby={titleId}
      tabIndex={-1}
      className={`flex flex-col rounded-ui-2xl border border-border bg-panel focus:outline-none ${
        wide ? 'max-w-[400px] flex-[1_1_340px] self-start' : 'h-full w-[min(400px,92vw)]'
      }`}
    >
      <div className="flex items-start gap-3 border-b border-divider p-5">
        {leading}
        <div className="min-w-0 flex-1">
          <h2 id={titleId} className="text-[18px] font-extrabold text-ink">
            {title}
          </h2>
          {sub && <div className="mt-0.5 text-[13px] text-ink-soft">{sub}</div>}
        </div>
        <IconButton label={t('steps.close')} onClick={onClose}>
          <Icon name="close" size={20} />
        </IconButton>
      </div>
      <div className="min-h-0 flex-1 divide-y divide-divider overflow-y-auto">{children}</div>
      {footer && <div className="flex gap-2 border-t border-divider p-5">{footer}</div>}
    </aside>
  );

  if (wide) return card;
  return createPortal(
    <div className="fixed inset-0 z-40 flex justify-end">
      <div className="absolute inset-0 animate-[scrim-in_200ms_ease-out]" style={{ background: 'var(--scrim)' }} onClick={onClose} aria-hidden="true" />
      <div className="relative h-full p-3 [animation:step-in-from-end_200ms_ease-out]">{card}</div>
    </div>,
    document.body,
  );
}

/** A section inside a drawer: 15px/800 heading + content. */
export function DrawerSection({ title, children }: { title?: ReactNode; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3 p-5">
      {title && <h3 className="text-[15px] font-extrabold text-ink">{title}</h3>}
      {children}
    </section>
  );
}
