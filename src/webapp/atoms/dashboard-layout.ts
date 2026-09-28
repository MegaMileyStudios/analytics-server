export const GRID_COLUMNS = 6;

export type WidgetWidth = 2 | 3 | 4 | 6;

export type WidgetLayout = {
  row: number;
  col: number;
  width: WidgetWidth;
};

export type PlacedItem = {
  id: string;
  layout: WidgetLayout;
};

export function allowedStarts(width: WidgetWidth): number[] {
  switch (width) {
    case 6:
      return [0];
    case 4:
      return [0, 2];
    case 3:
      return [0, 3];
    case 2:
      return [0, 2, 4];
  }
}

function overlaps(a: WidgetLayout, b: WidgetLayout): boolean {
  return a.row === b.row && a.col < b.col + b.width && b.col < a.col + a.width;
}

export function fits(items: PlacedItem[], candidate: WidgetLayout, ignoreId?: string): boolean {
  if (candidate.row < 0 || !allowedStarts(candidate.width).includes(candidate.col)) return false;
  return items.every((item) => item.id === ignoreId || !overlaps(item.layout, candidate));
}

export function compactRows<T extends PlacedItem>(items: T[]): void {
  const rows = [...new Set(items.map((x) => x.layout.row))].sort((a, b) => a - b);
  for (const item of items) {
    item.layout.row = rows.indexOf(item.layout.row);
  }
}

export function nextFreeRow(items: PlacedItem[]): number {
  return items.reduce((max, item) => Math.max(max, item.layout.row + 1), 0);
}

export function flowLayout(widths: WidgetWidth[]): WidgetLayout[] {
  const result: WidgetLayout[] = [];
  let row = 0;
  let col = 0;
  for (const width of widths) {
    let start = allowedStarts(width).find((s) => s >= col && s + width <= GRID_COLUMNS);
    if (start === undefined) {
      row++;
      start = 0;
    }
    result.push({ row, col: start, width });
    col = start + width;
  }
  return result;
}

export function sortByPosition<T extends PlacedItem>(items: T[]): T[] {
  return items.toSorted((a, b) => a.layout.row - b.layout.row || a.layout.col - b.layout.col);
}

const ALL_WIDTHS: WidgetWidth[] = [2, 3, 4, 6];

export function resizeLayout(
  items: PlacedItem[],
  id: string,
  current: WidgetLayout,
  edge: "left" | "right",
  rawEdge: number
): WidgetLayout {
  const fixedEdge = edge === "left" ? current.col + current.width : current.col;
  let best = current;
  let bestCost = Number.POSITIVE_INFINITY;

  for (const width of ALL_WIDTHS) {
    for (const col of allowedStarts(width)) {
      const candidate = { row: current.row, col, width };
      if (!fits(items, candidate, id)) continue;

      const moving = edge === "left" ? col : col + width;
      const fixed = edge === "left" ? col + width : col;
      const cost = Math.abs(moving - rawEdge) + 2 * Math.abs(fixed - fixedEdge);
      if (cost < bestCost) {
        best = candidate;
        bestCost = cost;
      }
    }
  }

  return best;
}

export function moveLayout(
  items: PlacedItem[],
  id: string,
  current: WidgetLayout,
  row: number,
  rawCol: number
): { layout: WidgetLayout; valid: boolean } {
  const byDistance = allowedStarts(current.width).toSorted((a, b) => Math.abs(a - rawCol) - Math.abs(b - rawCol));
  const free = byDistance.find((col) => fits(items, { ...current, row, col }, id));

  return free === undefined
    ? { layout: { ...current, row, col: byDistance[0] }, valid: false }
    : { layout: { ...current, row, col: free }, valid: true };
}
