import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';

// Hand-drawn SVG rather than a charting library: the page has exactly two small charts and
// a few bar lists, all single-series, and drawing them directly keeps them on the app's own
// tokens (so they follow light/dark mode for free) with nothing new to install.

/**
 * Width of an element, tracked as it resizes - the charts draw at real pixel size so text
 * never scales. A callback ref (not useRef) so the observer attaches whenever the element
 * actually appears: a chart first rendered in its "nothing to show" state has no element yet.
 */
function useWidth() {
  const [element, setElement] = useState<HTMLDivElement | null>(null);
  const [width, setWidth] = useState(0);

  useEffect(() => {
    if (!element) return;
    setWidth(element.clientWidth);
    const observer = new ResizeObserver((entries) => setWidth(entries[0].contentRect.width));
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return [setElement, width] as const;
}

export interface WeekPoint {
  /** Short label for the x axis (the week's first day). */
  label: string;
  /** Null draws a gap - a week with nothing to judge by is not a zero. */
  value: number | null;
  /** Lines shown while hovering this week. */
  tooltip: string[];
}

interface WeekChartProps {
  points: WeekPoint[];
  /** Top of the y axis. */
  max: number;
  ticks: number[];
  formatTick: (value: number) => string;
  /** Text drawn at the last point of a line chart. */
  endLabel?: string;
  ariaLabel: string;
  emptyText: string;
}

const HEIGHT = 220;
const PAD = { left: 40, right: 16, top: 16, bottom: 28 };

function useWeekChart(points: WeekPoint[], max: number) {
  const [ref, width] = useWidth();
  const [hovered, setHovered] = useState<number | null>(null);
  const innerWidth = Math.max(0, width - PAD.left - PAD.right);
  const innerHeight = HEIGHT - PAD.top - PAD.bottom;
  const slot = points.length ? innerWidth / points.length : 0;
  const x = (index: number) => PAD.left + slot * (index + 0.5);
  const y = (value: number) => PAD.top + innerHeight * (1 - value / max);
  // Thin the x labels out when the weeks get too close to read.
  const labelEvery = slot > 0 ? Math.max(1, Math.ceil(46 / slot)) : 1;
  return { ref, width, hovered, setHovered, innerWidth, innerHeight, slot, x, y, labelEvery };
}

function Frame({
  chart,
  points,
  ticks,
  formatTick,
  ariaLabel,
  children,
}: {
  chart: ReturnType<typeof useWeekChart>;
  points: WeekPoint[];
  ticks: number[];
  formatTick: (value: number) => string;
  ariaLabel: string;
  children: ReactNode;
}) {
  const { ref, width, hovered, setHovered, innerHeight, slot, x, y, labelEvery } = chart;
  const hoveredPoint = hovered === null ? null : points[hovered];

  return (
    // Time runs left to right in both languages, so the chart itself is always LTR - same
    // convention as the rest of the app's fixed-direction widgets.
    <div ref={ref} dir="ltr" className="relative">
      {width > 0 && (
        <svg width={width} height={HEIGHT} role="img" aria-label={ariaLabel} className="block">
          {ticks.map((tick) => (
            <g key={tick}>
              <line x1={PAD.left} x2={width - PAD.right} y1={y(tick)} y2={y(tick)} stroke="var(--border)" strokeWidth={1} opacity={0.6} />
              <text x={PAD.left - 6} y={y(tick) + 4} textAnchor="end" fontSize={12} fill="var(--ink-soft)" className="font-mono font-normal">
                {formatTick(tick)}
              </text>
            </g>
          ))}
          {points.map((point, index) =>
            index % labelEvery === 0 ? (
              <text key={index} x={x(index)} y={HEIGHT - 8} textAnchor="middle" fontSize={12} fill="var(--ink-soft)" className="font-normal">
                {point.label}
              </text>
            ) : null,
          )}
          {children}
          {points.map((_, index) => (
            <rect
              key={index}
              x={x(index) - slot / 2}
              y={PAD.top}
              width={slot}
              height={innerHeight}
              fill={hovered === index ? 'var(--ink)' : 'transparent'}
              opacity={hovered === index ? 0.05 : 1}
              onMouseEnter={() => setHovered(index)}
              onMouseLeave={() => setHovered(null)}
            />
          ))}
        </svg>
      )}
      {hoveredPoint && hovered !== null && (
        <div
          className="pointer-events-none absolute top-0 z-10 max-w-[220px] rounded bg-ink px-2 py-1.5 text-xs font-normal text-panel"
          style={x(hovered) > width / 2 ? { right: width - x(hovered) + 10 } : { left: x(hovered) + 10 }}
        >
          {hoveredPoint.tooltip.map((line, index) => (
            <div key={index} className={index === 0 ? 'font-semibold' : ''}>
              {line}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** A single line with a light wash underneath; breaks where a week has no value. */
export function WeekLineChart({ points, max, ticks, formatTick, endLabel, ariaLabel, emptyText }: WeekChartProps) {
  const chart = useWeekChart(points, max);
  if (!points.some((point) => point.value !== null)) return <p className="text-sm text-ink-soft">{emptyText}</p>;

  const { x, y, width } = chart;
  const segments: { index: number; value: number }[][] = [];
  let current: { index: number; value: number }[] = [];
  points.forEach((point, index) => {
    if (point.value === null) {
      if (current.length) segments.push(current);
      current = [];
    } else {
      current.push({ index, value: point.value });
    }
  });
  if (current.length) segments.push(current);
  const last = segments[segments.length - 1][segments[segments.length - 1].length - 1];

  return (
    <Frame chart={chart} points={points} ticks={ticks} formatTick={formatTick} ariaLabel={ariaLabel}>
      {segments.map((segment, segmentIndex) => {
        const line = segment.map((p, i) => `${i ? 'L' : 'M'}${x(p.index)} ${y(p.value)}`).join(' ');
        const first = segment[0];
        const end = segment[segment.length - 1];
        return (
          <g key={segmentIndex}>
            <path d={`${line} L${x(end.index)} ${y(0)} L${x(first.index)} ${y(0)} Z`} fill="var(--accent)" opacity={0.12} />
            <path d={line} fill="none" stroke="var(--accent)" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
            {segment.length === 1 && <circle cx={x(first.index)} cy={y(first.value)} r={3} fill="var(--accent)" />}
          </g>
        );
      })}
      <circle cx={x(last.index)} cy={y(last.value)} r={4.5} fill="var(--accent)" stroke="var(--panel)" strokeWidth={2} />
      {endLabel && (
        <text
          x={Math.min(x(last.index), width - PAD.right - 16)}
          y={Math.max(12, y(last.value) - 10)}
          textAnchor="middle"
          fontSize={13}
          fill="var(--ink)"
          className="font-mono font-normal"
        >
          {endLabel}
        </text>
      )}
    </Frame>
  );
}

/** One column per week, rounded at the top, growing from the baseline. */
export function WeekColumnChart({ points, max, ticks, formatTick, ariaLabel, emptyText }: WeekChartProps) {
  const chart = useWeekChart(points, max);
  if (!points.some((point) => point.value)) return <p className="text-sm text-ink-soft">{emptyText}</p>;

  const { x, y, slot } = chart;
  const barWidth = Math.max(2, Math.min(24, slot - 4));
  const radius = Math.min(3, barWidth / 2);

  return (
    <Frame chart={chart} points={points} ticks={ticks} formatTick={formatTick} ariaLabel={ariaLabel}>
      {points.map((point, index) => {
        if (!point.value) return null;
        const left = x(index) - barWidth / 2;
        const right = x(index) + barWidth / 2;
        const top = y(point.value);
        const base = y(0);
        const r = Math.min(radius, base - top);
        return (
          <path
            key={index}
            d={`M${left} ${base} V${top + r} Q${left} ${top} ${left + r} ${top} H${right - r} Q${right} ${top} ${right} ${top + r} V${base} Z`}
            fill="var(--accent)"
          />
        );
      })}
    </Frame>
  );
}

export interface BarItem {
  label: string;
  value: number;
  /** The figure shown at the end of the row. */
  text: string;
  /** Extra detail on hover. */
  title: string;
  tone?: 'accent' | 'danger';
}

/**
 * Labelled horizontal bars. Plain HTML rather than SVG so Arabic labels wrap and align the
 * way the rest of the page's text does, and the bars grow from the reading direction's start.
 */
export function BarList({ items, max, emptyText }: { items: BarItem[]; max: number; emptyText: string }) {
  if (items.length === 0) return <p className="text-sm text-ink-soft">{emptyText}</p>;

  return (
    <div className="space-y-2.5">
      {items.map((item, index) => (
        <div
          key={index}
          title={item.title}
          className="grid grid-cols-[minmax(0,45%)_minmax(40px,1fr)_auto] items-center gap-3 sm:grid-cols-[minmax(0,38%)_minmax(60px,1fr)_auto]"
        >
          <span dir="auto" className="min-w-0 text-sm font-normal text-ink">
            {item.label}
          </span>
          <span className="relative h-4">
            <span
              className={`absolute inset-y-0 start-0 rounded-e ${item.tone === 'danger' ? 'bg-danger' : 'bg-accent'}`}
              style={{ width: `${Math.max(1, (100 * item.value) / max)}%` }}
            />
          </span>
          <span className="min-w-[2.5rem] text-end font-mono text-xs font-normal text-ink">{item.text}</span>
        </div>
      ))}
    </div>
  );
}
