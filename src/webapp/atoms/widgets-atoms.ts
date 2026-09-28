import { produce } from "immer";
import { atom, WritableAtom } from "jotai";
import {
  compactRows,
  fits,
  flowLayout,
  nextFreeRow,
  sortByPosition,
  WidgetLayout,
  WidgetWidth,
} from "./dashboard-layout";

export type EventsChartWidgetConfig = {
  selectedEventNames: string[];
};

export type PropertyAggregation = "max" | "min" | "avg" | "median" | "sum" | "p99" | "p95";

export type PropertySeries = {
  propKey: string;
  aggregation: PropertyAggregation;
  color: string;
};

export type PropertyChartWidgetConfig = {
  eventName?: string;
  kind?: "number" | "boolean" | "string";
  series?: PropertySeries[];
  stackMode?: "none" | "stacked" | "filled";
  propKey?: string;
  display?: "bar" | "bar-time" | "pie";
};

export type WidgetType =
  | "custom-events-chart"
  | "custom-property-chart"
  | "events-chart"
  | "countries"
  | "operating-systems"
  | "events"
  | "app-versions";
const userDefinedWidgetTypes: WidgetType[] = ["custom-events-chart", "custom-property-chart"];

export type SingleWidgetConfig<T = any> = {
  id: string;
  title: string;
  type: WidgetType;
  isDefined: boolean;
  isMinimized: boolean;
  layout: WidgetLayout;
  orderIndex?: number;
  properties?: T;
  supportsRemove?: boolean;
};

export type WidgetsConfig = {
  [key: string]: SingleWidgetConfig[];
};

const defaultWidth = (type: WidgetType): WidgetWidth =>
  type === "custom-events-chart" || type === "custom-property-chart" || type === "events-chart" ? 6 : 3;

export const DEFAULT_WIDGETS_CONFIG: SingleWidgetConfig[] = [
  {
    id: "events-chart",
    title: "Custom Chart",
    type: "custom-events-chart",
    isMinimized: false,
    layout: { row: 0, col: 0, width: 6 },
    isDefined: false,
    properties: {
      selectedEventNames: [],
    },
    supportsRemove: true,
  },
  {
    id: "main-chart",
    title: "Events Chart",
    type: "events-chart",
    isMinimized: false,
    layout: { row: 1, col: 0, width: 6 },
    isDefined: true,
  },
  {
    id: "country",
    title: "Countries",
    type: "countries",
    isMinimized: false,
    layout: { row: 2, col: 0, width: 3 },
    isDefined: true,
  },
  {
    id: "os",
    title: "Operating Systems",
    type: "operating-systems",
    isMinimized: false,
    layout: { row: 2, col: 3, width: 3 },
    isDefined: true,
  },
  {
    id: "event",
    title: "Events",
    type: "events",
    isMinimized: false,
    layout: { row: 3, col: 0, width: 3 },
    isDefined: true,
  },
  {
    id: "version",
    title: "App Versions",
    type: "app-versions",
    isMinimized: false,
    layout: { row: 3, col: 3, width: 3 },
    isDefined: true,
  },
];

const EMPTY_CUSTOM_EVENTS_CHART_WIDGET: SingleWidgetConfig<EventsChartWidgetConfig> = {
  id: "events-chart",
  title: "Custom Chart",
  type: "custom-events-chart",
  isMinimized: false,
  layout: { row: 0, col: 0, width: 6 },
  isDefined: false,
  properties: {
    selectedEventNames: [],
  },
  supportsRemove: true,
};

const withLayout = (widgets: SingleWidgetConfig[]): SingleWidgetConfig[] => {
  if (widgets.every((w) => w.layout)) return widgets;

  const ordered = widgets.toSorted((a, b) => (a.orderIndex ?? 0) - (b.orderIndex ?? 0));
  const layouts = flowLayout(ordered.map((w) => defaultWidth(w.type)));
  return ordered.map(({ orderIndex, ...w }, index) => ({ ...w, layout: layouts[index] }));
};

localStorage.removeItem("dashboard_widgets");

const initialWidgetsValue = JSON.parse(localStorage.getItem("dashboard_widgets_map") ?? "null");
const getDashboardWidgetAtom: WritableAtom<WidgetsConfig, WidgetsConfig[], void> = atom(initialWidgetsValue);

export const getDashboardWidgetsForAppAtom = atom((get) => {
  const widgets = get(getDashboardWidgetAtom);
  const getWidgetsForApp = (appId: string) => {
    const widgetWithAppId = widgets?.[appId];
    return widgetWithAppId ? withLayout(widgetWithAppId) : DEFAULT_WIDGETS_CONFIG;
  };
  return getWidgetsForApp;
});

export type UpdateDashboardWidgetsAction<T = any> =
  | {
      widgetId: string;
      appId: string;
      type: "update-properties";
      properties?: T;
    }
  | {
      widgetId: string;
      appId: string;
      type: "move";
      row: number;
      col: number;
    }
  | {
      widgetId: string;
      appId: string;
      type: "resize";
      col: number;
      width: WidgetWidth;
    }
  | {
      widgetId: string;
      appId: string;
      type: "change-type";
      widgetType: WidgetType;
      properties?: T;
    }
  | {
      widgetId: string;
      appId: string;
      type: "toggle-minimized";
    }
  | {
      widgetId: string;
      appId: string;
      type: "toggle-is-defined";
    };

export const dashboardWidgetsAtom = atom<null, [UpdateDashboardWidgetsAction], void>(
  (get) => null,
  (get, set, action) => {
    const widgetsConfig = get(getDashboardWidgetAtom);
    const mapResult = widgetsConfig ?? {};
    const widgetsConfigArray: SingleWidgetConfig[] = withLayout(mapResult[action.appId] ?? DEFAULT_WIDGETS_CONFIG);

    const result = produce(widgetsConfigArray, (draft) => {
      let widgetIndex = draft.findIndex((w) => w.id === action.widgetId);
      if (widgetIndex === -1) {
        draft.push({
          id: action.widgetId,
          title: action.widgetId,
          type: "custom-events-chart",
          isMinimized: false,
          layout: { row: nextFreeRow(draft), col: 0, width: 6 },
          isDefined: true,
        });
        widgetIndex = draft.length - 1;
      }
      const widget = draft[widgetIndex];

      if (action.type === "update-properties") {
        widget.properties = action.properties;
      } else if (action.type === "move") {
        const layout = { ...widget.layout, row: action.row, col: action.col };
        if (fits(draft, layout, widget.id)) {
          widget.layout = layout;
          compactRows(draft);
        }
      } else if (action.type === "resize") {
        const layout = { ...widget.layout, col: action.col, width: action.width };
        if (fits(draft, layout, widget.id)) {
          widget.layout = layout;
        }
      } else if (action.type === "change-type") {
        widget.type = action.widgetType;
        widget.properties = action.properties;
      } else if (action.type === "toggle-minimized") {
        widget.isMinimized = !widget.isMinimized;
      } else if (action.type === "toggle-is-defined") {
        widget.isDefined = !widget.isDefined;
        widget.isMinimized = false;

        if (widget.isDefined) {
          const areAllUserDefinedWidgetsDefined = draft
            .filter((w) => userDefinedWidgetTypes.includes(w.type))
            .every((w) => w.isDefined);
          if (areAllUserDefinedWidgetsDefined) {
            const nextRow = nextFreeRow(draft);
            draft.push(
              produce(EMPTY_CUSTOM_EVENTS_CHART_WIDGET, (customEventsDraft) => {
                customEventsDraft.id = getNextWidgetId(widgetsConfigArray, customEventsDraft.id);
                customEventsDraft.layout = { row: nextRow, col: 0, width: 6 };
              })
            );
          }
        } else {
          if (widget.type === "custom-property-chart") {
            widget.type = "custom-events-chart";
            widget.properties = { selectedEventNames: [] };
          }

          const widgetsToRemoveIndexes = draft
            .map((w, index) => ({ w, index }))
            .filter(({ w }) => userDefinedWidgetTypes.includes(w.type) && !w.isDefined && w.id !== action.widgetId)
            .map(({ index }) => index)
            .reverse();
          widgetsToRemoveIndexes.forEach((toRemoveIndex) => {
            draft.splice(toRemoveIndex, 1);
          });
          compactRows(draft);
        }
      }
    });

    const allConfigs = produce(mapResult, (draftResult: any) => {
      draftResult[action.appId] = sortByPosition(result);
    });
    set(getDashboardWidgetAtom, allConfigs);

    localStorage.setItem("dashboard_widgets_map", JSON.stringify(allConfigs));
  }
);

const getNextWidgetId = (existingWidgets: SingleWidgetConfig[], baseId: string): string => {
  const pattern = new RegExp(`^${baseId}-(\\d+)$`);
  const existingNumbers = existingWidgets
    .map((widget) => {
      const match = widget.id.match(pattern);
      return match ? parseInt(match[1], 10) : null;
    })
    .filter((num): num is number => num !== null);

  const maxNumber = existingNumbers.length > 0 ? Math.max(...existingNumbers) : 0;
  return `${baseId}-${maxNumber + 1}`;
};
