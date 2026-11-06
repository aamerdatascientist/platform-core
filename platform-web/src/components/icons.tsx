import type { ReactNode, SVGProps } from 'react';

/**
 * The redesign's icons (docs/redesign/screens/html): 24x24 viewBox, stroke 2 (checkmarks
 * 2.2-2.6), round caps and joins, currentColor. Directional icons are drawn pointing
 * "forward" in English (right) and flip in Arabic via the `rtl-flip` class, so a forward
 * chevron always points in the reading direction. Decorative by default (aria-hidden); pass
 * `title` for an icon that carries meaning on its own.
 */
export type IconName =
  | 'globe' | 'moon' | 'sun' | 'eye' | 'eyeOff' | 'clipboard' | 'chevronForward' | 'chevronBack'
  | 'chevronDown' | 'check' | 'home' | 'list' | 'sliders' | 'close' | 'cloudCheck' | 'pin' | 'calendar'
  | 'plus' | 'minus' | 'search' | 'arrowForward' | 'arrowBack' | 'camera' | 'send' | 'lock' | 'logout'
  | 'alert' | 'wifiOff' | 'clock' | 'pencil' | 'grip' | 'bell' | 'expand' | 'external' | 'trash' | 'paperclip';

const paths: Record<IconName, { body: ReactNode; stroke?: number; flip?: boolean; fill?: boolean }> = {
  globe: { body: <><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18" /></> },
  moon: { body: <path d="M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z" /> },
  sun: { body: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></> },
  eye: { body: <><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></> },
  eyeOff: { body: <path d="M3 3l18 18M10.6 5.1A10.9 10.9 0 0 1 12 5c6 0 10 7 10 7a17.8 17.8 0 0 1-3.3 4.1M6.6 6.6C3.8 8.4 2 12 2 12s4 7 10 7a9.8 9.8 0 0 0 5.4-1.6M9.9 9.9a3 3 0 0 0 4.2 4.2" /> },
  clipboard: { body: <><rect x="5" y="4" width="14" height="17" rx="2" /><path d="M9 4h6v3H9zM9 12h6M9 16h4" /></> },
  chevronForward: { body: <path d="m9 18 6-6-6-6" />, stroke: 2.2, flip: true },
  chevronBack: { body: <path d="m15 18-6-6 6-6" />, stroke: 2.2, flip: true },
  chevronDown: { body: <path d="m6 9 6 6 6-6" /> },
  check: { body: <path d="M20 6 9 17l-5-5" />, stroke: 2.4 },
  home: { body: <path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6h-6v6H4a1 1 0 0 1-1-1z" /> },
  list: { body: <path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01" /> },
  sliders: { body: <><path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12" /><circle cx="16" cy="6" r="2" /><circle cx="10" cy="12" r="2" /><circle cx="18" cy="18" r="2" /></> },
  close: { body: <path d="M18 6 6 18M6 6l12 12" /> },
  cloudCheck: { body: <><path d="M7 18a5 5 0 1 1 1-9.9A6 6 0 0 1 19 10a4 4 0 0 1 0 8z" /><path d="m9 13 2 2 4-4" /></> },
  pin: { body: <><path d="M12 21s-7-6-7-11a7 7 0 0 1 14 0c0 5-7 11-7 11z" /><circle cx="12" cy="10" r="2.5" /></> },
  calendar: { body: <><rect x="3" y="5" width="18" height="16" rx="2" /><path d="M3 10h18M8 3v4M16 3v4" /></> },
  plus: { body: <path d="M12 5v14M5 12h14" />, stroke: 2.2 },
  minus: { body: <path d="M5 12h14" />, stroke: 2.2 },
  search: { body: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></> },
  arrowForward: { body: <path d="M5 12h14M13 6l6 6-6 6" />, flip: true },
  arrowBack: { body: <path d="M19 12H5M11 6l-6 6 6 6" />, flip: true },
  camera: { body: <><path d="M4 8h3l2-3h6l2 3h3v11H4z" /><circle cx="12" cy="13" r="3.5" /></> },
  send: { body: <path d="M22 2 11 13M22 2l-7 20-4-9-9-4z" />, flip: true },
  lock: { body: <><rect x="4" y="11" width="16" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></> },
  logout: { body: <path d="M15 4h4v16h-4M10 8l-4 4 4 4M6 12h10" />, flip: true },
  alert: { body: <><circle cx="12" cy="12" r="9" /><path d="M12 8v5M12 16h.01" /></> },
  wifiOff: { body: <path d="M2 2l20 20M8.5 16.5a5 5 0 0 1 7 0M5 13a10 10 0 0 1 4.2-2.6M19 13a10 10 0 0 0-2.4-1.7M2 8.8a15 15 0 0 1 4.2-2.7M22 8.8A15 15 0 0 0 11 5.1M12 20h.01" /> },
  clock: { body: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>, stroke: 2.2 },
  pencil: { body: <path d="M4 20h4L19 9l-4-4L4 16z" /> },
  grip: { body: <><circle cx="9" cy="6" r="1.6" /><circle cx="15" cy="6" r="1.6" /><circle cx="9" cy="12" r="1.6" /><circle cx="15" cy="12" r="1.6" /><circle cx="9" cy="18" r="1.6" /><circle cx="15" cy="18" r="1.6" /></>, fill: true },
  bell: { body: <path d="M6 9a6 6 0 0 1 12 0c0 6 3 7 3 7H3s3-1 3-7M10 20a2 2 0 0 0 4 0" /> },
  expand: { body: <path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" /> },
  external: { body: <path d="M7 17 17 7M9 7h8v8" />, flip: true },
  trash: { body: <path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13" /> },
  paperclip: { body: <path d="M21 11.5 12.5 20a5 5 0 0 1-7-7L14 4.5a3.5 3.5 0 0 1 5 5L10.5 18a2 2 0 0 1-3-3l7.5-7.5" /> },
};

export function Icon({
  name,
  size = 20,
  strokeWidth,
  title,
  className = '',
  ...rest
}: { name: IconName; size?: number; strokeWidth?: number; title?: string } & Omit<SVGProps<SVGSVGElement>, 'name'>) {
  const icon = paths[name];
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill={icon.fill ? 'currentColor' : 'none'}
      stroke={icon.fill ? 'none' : 'currentColor'}
      strokeWidth={strokeWidth ?? icon.stroke ?? 2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden={title ? undefined : true}
      role={title ? 'img' : undefined}
      className={`${icon.flip ? 'rtl-flip ' : ''}shrink-0 ${className}`}
      {...rest}
    >
      {title && <title>{title}</title>}
      {icon.body}
    </svg>
  );
}

/**
 * The Asas logo mark: three stacked rounded bars, each skewed -18deg (01, side menu). The large
 * sign-in tile is --panel with a border (72px: bars 34x8, gap 4, radius 20); the side menu's
 * small tile is --field-muted without one (34px: bars 16x4, gap 2, radius 10).
 */
export function LogoMark({ size = 34 }: { size?: number }) {
  const large = size >= 48;
  const bar = large ? { w: size * (34 / 72), h: size * (8 / 72), r: size * (3 / 72), gap: size * (4 / 72) }
    : { w: size * (16 / 34), h: size * (4 / 34), r: size * (2 / 34), gap: size * (2 / 34) };
  return (
    <span
      className={`inline-flex shrink-0 flex-col items-center justify-center ${large ? 'border border-border bg-panel' : 'bg-field-muted'}`}
      style={{ width: size, height: size, borderRadius: size * (large ? 20 / 72 : 10 / 34), gap: bar.gap }}
      aria-hidden="true"
    >
      {(['--logo-1', '--logo-2', '--logo-3'] as const).map((v) => (
        <span key={v} style={{ width: bar.w, height: bar.h, borderRadius: bar.r, background: `var(${v})`, transform: 'skewX(-18deg)' }} />
      ))}
    </span>
  );
}
