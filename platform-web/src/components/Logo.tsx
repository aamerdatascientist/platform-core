interface LogoProps {
  size?: 'sm' | 'lg';
}

/**
 * Placeholder brand mark - three ascending bars. Deliberately simple flat shapes, not a
 * finished asset, so replacing it later means swapping this one file, not hunting through
 * the app. "Nexus" is a placeholder product name too, not the real one.
 */
export function Logo({ size = 'sm' }: LogoProps) {
  const badge = size === 'lg' ? 56 : 22;
  const icon = size === 'lg' ? 32 : 13;

  return (
    <div
      style={{ width: badge, height: badge }}
      className="flex shrink-0 items-center justify-center rounded border border-sidebar-border bg-sidebar"
    >
      <svg viewBox="0 0 24 24" width={icon} height={icon} fill="none" aria-hidden="true">
        <rect x="3" y="13" width="4" height="8" className="fill-signal" />
        <rect x="10" y="8" width="4" height="13" className="fill-signal" />
        <rect x="17" y="3" width="4" height="18" className="fill-signal-light" />
      </svg>
    </div>
  );
}
