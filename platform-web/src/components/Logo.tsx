interface LogoProps {
  size?: 'sm' | 'lg';
}

/**
 * Three rounded bars, staggered and rotated together as one group. Two color
 * treatments, not one - the "lg" set uses the full dark-to-light brand range
 * (#0F1729 -> #4361EE -> #7B92FF) since it's only ever shown on a light
 * surface (the sign-in page). The "sm" set skips that darkest tone entirely
 * and uses a lighter three-tone range instead, because #0F1729 is nearly the
 * sidebar's own background color - using it there would make that bar
 * disappear, the same contrast bug already found and fixed once during
 * design exploration. Not a container/badge anymore, by design - stands
 * directly on whatever background it's placed on.
 */
export function Logo({ size = 'sm' }: LogoProps) {
  const px = size === 'lg' ? 56 : 22;
  const colors =
    size === 'lg' ? ['#0F1729', '#4361EE', '#7B92FF'] : ['#4361EE', '#6C7EF0', '#C3CEFF'];

  return (
    <svg viewBox="0 0 120 120" width={px} height={px} className="shrink-0" aria-hidden="true">
      <g transform="rotate(-8 60 60)">
        <rect x="18" y="66" width="80" height="22" rx="3" fill={colors[0]} />
        <rect x="26" y="42" width="80" height="22" rx="3" fill={colors[1]} />
        <rect x="34" y="18" width="80" height="22" rx="3" fill={colors[2]} />
      </g>
    </svg>
  );
}
