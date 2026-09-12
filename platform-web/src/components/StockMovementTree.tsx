import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { StockMovementBreakdownDto, StockMovementType } from '../types';

interface StockMovementTreeProps {
  movements: StockMovementBreakdownDto[];
}

const MOVEMENT_TYPES: StockMovementType[] = ['GoodsReceipt', 'MaterialIssue', 'StockTransfer', 'StockAdjustment'];

// Reused app-wide design tokens, not new colors invented for this one diagram - each
// movement type's whole branch (its node, its connector, its destination children) is
// colored and opacity-matched consistently.
const MOVEMENT_COLOR: Record<StockMovementType, string> = {
  GoodsReceipt: 'var(--success)',
  MaterialIssue: 'var(--accent)',
  StockTransfer: 'var(--accent2)',
  StockAdjustment: 'var(--danger)',
};

const ROW_HEIGHT = 34;
const BAR_HEIGHT = 12;
const MAX_BAR_WIDTH = 130;
const COLUMN_X = { root: 12, movement: 260, destination: 500 };
const LABEL_OFFSET = 6;
const VIEW_WIDTH = 720;
// Room for the topmost leaf's label, which sits above its bar and would otherwise clip
// against the SVG's own top edge.
const TOP_PADDING = 18;
const BOTTOM_PADDING = 6;

interface DestinationNode {
  id: string;
  label: string;
  value: number;
  y: number;
}

interface MovementNode {
  type: StockMovementType;
  label: string;
  value: number;
  y: number;
  color: string;
  destinations: DestinationNode[];
}

/**
 * A decomposition tree, one material at a time - "movement-type then destination" is a
 * clean 3-column tree for a single material, but every material at once would be an
 * unreadable mess of the same shape repeated dozens of times. The material picker below the
 * tree is what makes "per material" real without adding a fourth column to the layout.
 *
 * Layout, faithful to the reference algorithm (not re-derived): leaves get sequential row
 * Y-positions; each parent's Y is the average of its children's Y; the root's Y is the
 * average of its direct children's Y. Three fixed X columns. Each node is a <rect> sized
 * proportionally to its value relative to the max among its siblings, with its label above
 * the bar. Connectors are cubic beziers between parent/child edge midpoints, colored and
 * opacity-matched to their top-level branch.
 */
export function StockMovementTree({ movements }: StockMovementTreeProps) {
  const { t } = useTranslation();

  const materials = useMemo(() => {
    const byId = new Map<string, { id: string; code: string; name: string }>();
    for (const m of movements) {
      if (!byId.has(m.materialId)) byId.set(m.materialId, { id: m.materialId, code: m.materialCode, name: m.materialName });
    }
    return [...byId.values()].sort((a, b) => a.code.localeCompare(b.code));
  }, [movements]);

  const [selectedMaterialId, setSelectedMaterialId] = useState<string | null>(null);
  const activeMaterialId = selectedMaterialId ?? materials[0]?.id ?? null;
  const activeMaterial = materials.find((m) => m.id === activeMaterialId);

  const movementNodes = useMemo<MovementNode[]>(() => {
    if (!activeMaterialId) return [];
    const filtered = movements.filter((m) => m.materialId === activeMaterialId);

    const byType = MOVEMENT_TYPES.map((type) => ({
      type,
      rows: filtered
        .filter((m) => m.movementType === type)
        .sort((a, b) => Math.abs(b.totalQuantity) - Math.abs(a.totalQuantity)),
    })).filter((group) => group.rows.length > 0);

    // Sequential row position across every leaf, grouped by movement type in a fixed order.
    let leafIndex = 0;
    return byType.map(({ type, rows }) => {
      const destinations: DestinationNode[] = rows.map((row) => {
        const y = TOP_PADDING + leafIndex * ROW_HEIGHT + ROW_HEIGHT / 2;
        leafIndex += 1;
        return { id: row.locationId, label: row.locationName, value: row.totalQuantity, y };
      });

      return {
        type,
        label: t(`executiveOverview.stockMovement.movementType.${type}`),
        value: destinations.reduce((sum, d) => sum + d.value, 0),
        y: destinations.reduce((sum, d) => sum + d.y, 0) / destinations.length,
        color: MOVEMENT_COLOR[type],
        destinations,
      };
    });
  }, [movements, activeMaterialId, t]);

  const leafCount = movementNodes.reduce((sum, node) => sum + node.destinations.length, 0);
  const rootY = movementNodes.length === 0 ? 0 : movementNodes.reduce((sum, n) => sum + n.y, 0) / movementNodes.length;
  const viewHeight = Math.max(leafCount * ROW_HEIGHT, ROW_HEIGHT) + TOP_PADDING + BOTTOM_PADDING;
  const maxMovementValue = Math.max(1, ...movementNodes.map((n) => Math.abs(n.value)));

  if (materials.length === 0) {
    return <p className="text-sm text-ink-soft">{t('executiveOverview.stockMovement.empty')}</p>;
  }

  return (
    <div>
      <select
        className="mb-4 w-full max-w-xs rounded border border-border bg-bg px-3 py-2 text-sm focus:border-accent focus:outline-none"
        value={activeMaterialId ?? ''}
        onChange={(e) => setSelectedMaterialId(e.target.value)}
      >
        {materials.map((material) => (
          <option key={material.id} value={material.id}>
            {material.code} — {material.name}
          </option>
        ))}
      </select>

      {movementNodes.length === 0 ? (
        <p className="text-sm text-ink-soft">{t('executiveOverview.stockMovement.noMovements')}</p>
      ) : (
        // dir="ltr" pinned on a wrapper, not the <svg> itself (React's SVG typings don't
        // accept a dir prop, even though the DOM attribute is valid) - this is a
        // left-to-right data-flow diagram (root -> movement -> destination) regardless of
        // UI language, same reasoning as keeping the toggle row's physical order fixed
        // elsewhere in this app. Without it, an inherited RTL context can flip SVG
        // text-anchor/BiDi resolution for the text labels even though the rect/path
        // geometry itself is direction-agnostic; dir cascades via CSS direction the same
        // way whether set on the svg or a wrapping element.
        <div dir="ltr">
          <svg viewBox={`0 0 ${VIEW_WIDTH} ${viewHeight}`} width="100%" height={viewHeight} role="img">
          {/* Connectors first, so every node's bar renders on top of the curves reaching it. */}
          {movementNodes.map((node) => (
            <g key={`connectors-${node.type}`}>
              <Connector x1={COLUMN_X.root + MAX_BAR_WIDTH} y1={rootY} x2={COLUMN_X.movement} y2={node.y} color={node.color} opacity={0.5} />
              {node.destinations.map((dest) => (
                <Connector
                  key={`c-${node.type}-${dest.id}`}
                  x1={COLUMN_X.movement + barWidth(node.value, maxMovementValue)}
                  y1={node.y}
                  x2={COLUMN_X.destination}
                  y2={dest.y}
                  color={node.color}
                  opacity={0.35}
                />
              ))}
            </g>
          ))}

          <TreeBar x={COLUMN_X.root} y={rootY} width={MAX_BAR_WIDTH} color="var(--ink-soft)" opacity={0.4} label={activeMaterial?.code ?? ''} />

          {movementNodes.map((node) => (
            <g key={node.type}>
              <TreeBar
                x={COLUMN_X.movement}
                y={node.y}
                width={barWidth(node.value, maxMovementValue)}
                color={node.color}
                opacity={0.85}
                label={`${node.label} (${formatQuantity(node.value)})`}
              />
              {node.destinations.map((dest) => {
                const maxDestValue = Math.max(1, ...node.destinations.map((d) => Math.abs(d.value)));
                return (
                  <TreeBar
                    key={dest.id}
                    x={COLUMN_X.destination}
                    y={dest.y}
                    width={barWidth(dest.value, maxDestValue)}
                    color={node.color}
                    opacity={0.6}
                    label={`${dest.label} (${formatQuantity(dest.value)})`}
                  />
                );
              })}
            </g>
          ))}
          </svg>
        </div>
      )}
    </div>
  );
}

function barWidth(value: number, maxSiblingValue: number) {
  return (Math.abs(value) / maxSiblingValue) * MAX_BAR_WIDTH;
}

function formatQuantity(value: number) {
  return value.toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function TreeBar({ x, y, width, color, opacity, label }: { x: number; y: number; width: number; color: string; opacity: number; label: string }) {
  return (
    <g>
      <text x={x} y={y - BAR_HEIGHT / 2 - LABEL_OFFSET} fontSize={10} fill="var(--ink)" className="font-mono">
        {label}
      </text>
      <rect x={x} y={y - BAR_HEIGHT / 2} width={Math.max(width, 2)} height={BAR_HEIGHT} rx={2} fill={color} fillOpacity={opacity} />
    </g>
  );
}

function Connector({ x1, y1, x2, y2, color, opacity }: { x1: number; y1: number; x2: number; y2: number; color: string; opacity: number }) {
  const midX = (x1 + x2) / 2;
  return <path d={`M ${x1},${y1} C ${midX},${y1} ${midX},${y2} ${x2},${y2}`} stroke={color} strokeOpacity={opacity} strokeWidth={1.5} fill="none" />;
}
