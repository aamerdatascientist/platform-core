interface LoadingSpinnerProps {
  size?: 'sm' | 'md' | 'lg';
}

const SIZES = { sm: 20, md: 36, lg: 64 };

/**
 * Same mark as Logo, animated - each of the three bars builds in from below,
 * holds, then dissolves upward, staggered 0.2s apart so the motion reads as
 * "layer, then layer, then layer" rather than all three moving in lockstep.
 * Colors are the lighter, dark-background-safe set (see Logo.tsx) since this
 * shows up in places that sit on both light and dark surfaces.
 */
export function LoadingSpinner({ size = 'md' }: LoadingSpinnerProps) {
  const px = SIZES[size];
  const colors = ['#4361EE', '#6C7EF0', '#C3CEFF'];
  const positions = [
    { x: 18, y: 66, delay: '0s' },
    { x: 26, y: 42, delay: '0.2s' },
    { x: 34, y: 18, delay: '0.4s' },
  ];

  return (
    <svg viewBox="0 0 120 120" width={px} height={px} aria-hidden="true">
      <g transform="rotate(-8 60 60)">
        {positions.map((p, i) => (
          <rect
            key={i}
            x={p.x}
            y={p.y}
            width="80"
            height="22"
            rx="3"
            fill={colors[i]}
            className="origin-center animate-layer-build"
            style={{ animationDelay: p.delay }}
          />
        ))}
      </g>
    </svg>
  );
}
