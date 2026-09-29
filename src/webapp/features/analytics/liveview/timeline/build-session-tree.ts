export type TimelineEventNode = {
  kind: "event";
  name: string;
  timestamp: Date;
  stringProps?: string;
  numericProps?: string;
  // Milliseconds since the previous item of the timeline, 0 for the first item
  distance: number;
  isFirst: boolean;
};

export type TimelineBlockNode = {
  kind: "main" | "popup";
  name: string;
  timestamp: Date;
  // Undefined while the view is still open in a live session
  endedAt?: Date;
  distance: number;
  isFirst: boolean;
  children: TimelineNode[];
};

export type TimelineCloseNode = {
  kind: "close";
  name: string;
  timestamp: Date;
  // No PopupClose was received, the close was implied by a main view change or the session end
  implied: boolean;
  // A PopupClose for a popup that wasn't open
  stray: boolean;
  distance: number;
};

export type TimelineNode = TimelineEventNode | TimelineBlockNode | TimelineCloseNode;

export type SessionTreeInput = {
  eventsName: string[];
  eventsTimestamp: string[];
  eventsStringProps: string[];
  eventsNumericProps: string[];
  viewsType?: string[];
  viewsName?: string[];
  viewsTimestamp?: string[];
};

type Item =
  | { kind: "event"; timestamp: Date; index: number }
  | { kind: "view"; timestamp: Date; index: number; type: string; name: string };

// Merges the regular events and the view events of a session into a tree: every main view is a
// top level block, popups are nested inside the view (or popup) they were opened on, and events
// are placed inside whatever was on top when they happened.
export function buildSessionTree(input: SessionTreeInput, sessionEnded: boolean): TimelineNode[] {
  const items: Item[] = [
    ...(input.viewsTimestamp ?? []).map((ts, index) => ({
      kind: "view" as const,
      timestamp: new Date(ts),
      index,
      type: input.viewsType?.[index] ?? "",
      name: input.viewsName?.[index] ?? "",
    })),
    ...(input.eventsTimestamp ?? []).map((ts, index) => ({ kind: "event" as const, timestamp: new Date(ts), index })),
  ];

  // Regular events only have second precision, so when they share a second with a view event the
  // real order is unknown. The sort is stable and views come first, so views win those ties.
  items.sort((a, b) => a.timestamp.getTime() - b.timestamp.getTime());

  const root: TimelineNode[] = [];
  let currentMain: TimelineBlockNode | undefined;
  const popups: TimelineBlockNode[] = [];
  let previous: Date | undefined;

  const container = () => (popups.length > 0 ? popups[popups.length - 1] : currentMain)?.children ?? root;

  const closePopup = (popup: TimelineBlockNode, at: Date, implied: boolean) => {
    popup.endedAt = at;
    popup.children.push({ kind: "close", name: popup.name, timestamp: at, implied, stray: false, distance: 0 });
  };

  // Closes the popups above (and excluding) the given depth, innermost first
  const closePopupsAbove = (depth: number, at: Date) => {
    while (popups.length > depth) closePopup(popups.pop()!, at, true);
  };

  for (const item of items) {
    const distance = previous ? item.timestamp.getTime() - previous.getTime() : 0;
    const isFirst = !previous;
    previous = item.timestamp;

    if (item.kind === "event") {
      container().push({
        kind: "event",
        name: input.eventsName?.[item.index] ?? "",
        timestamp: item.timestamp,
        stringProps: input.eventsStringProps?.[item.index],
        numericProps: input.eventsNumericProps?.[item.index],
        distance,
        isFirst,
      });
      continue;
    }

    if (item.type === "MainScreenChange") {
      closePopupsAbove(0, item.timestamp);
      if (currentMain) currentMain.endedAt = item.timestamp;
      currentMain = { kind: "main", name: item.name, timestamp: item.timestamp, distance, isFirst, children: [] };
      root.push(currentMain);
    } else if (item.type === "PopupOpen") {
      const popup: TimelineBlockNode = {
        kind: "popup",
        name: item.name,
        timestamp: item.timestamp,
        distance,
        isFirst,
        children: [],
      };
      container().push(popup);
      popups.push(popup);
    } else if (item.type === "PopupClose") {
      const depth = popups.map((p) => p.name).lastIndexOf(item.name);
      if (depth >= 0) {
        closePopupsAbove(depth + 1, item.timestamp);
        const popup = popups.pop()!;
        closePopup(popup, item.timestamp, false);
        popup.children[popup.children.length - 1].distance = distance;
      } else {
        container().push({ kind: "close", name: item.name, timestamp: item.timestamp, implied: false, stray: true, distance });
      }
    }
  }

  // Once the session is over, whatever was still open ended with the last thing that happened
  if (sessionEnded && previous) {
    closePopupsAbove(0, previous);
    if (currentMain) currentMain.endedAt = previous;
  }

  return root;
}
