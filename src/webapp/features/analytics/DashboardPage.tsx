import { LazyLoad } from "@components/LazyLoad";
import { Page, PageHeading } from "@components/Page";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@components/Tooltip";
import { DndContext, DragMoveEvent, MouseSensor, useSensor, useSensors } from "@dnd-kit/core";
import { getEventCoordinates } from "@dnd-kit/utilities";
import { useApps, useCurrentApp } from "@features/apps";
import { IconShare } from "@tabler/icons-react";
import { useAtomValue, useSetAtom } from "jotai/react";
import { CSSProperties, ReactNode, useMemo, useRef, useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import {
  GRID_COLUMNS,
  moveLayout,
  nextFreeRow,
  resizeLayout,
  sortByPosition,
  WidgetLayout,
} from "../../atoms/dashboard-layout";
import { dashboardWidgetsAtom, getDashboardWidgetsForAppAtom, SingleWidgetConfig } from "../../atoms/widgets-atoms";
import { CurrentFilters } from "./CurrentFilters";
import { AppShareInfo } from "./dashboard/AppShareInfo";
import { CountryWidget } from "./dashboard/CountryWidget";
import { EventWidget } from "./dashboard/EventWidget";
import { OSWidget } from "./dashboard/OSWidget";
import { OnboardingDashboard } from "./dashboard/OnboardingDashboard";
import { TeaserDashboardContainer } from "./dashboard/TeaserDashboardContainer";
import { VersionWidget } from "./dashboard/VersionWidget";
import { ResizeEdge, WidgetContainer } from "./dashboard/WidgetContainer";
import { EventsChartWidget } from "./dashboard/custom-widgets/EventsChartWidget";
import { PropertyChartWidget } from "./dashboard/custom-widgets/PropertyChartWidget";
import { DateFilterContainer } from "./date-filters/DateFilterContainer";
import { MainChartWidget } from "./key_metrics/MainChartWidget";
import { AppLockedContent } from "./locked/AppLockedContent";
import { BuildModeSelector } from "./mode/BuildModeSelector";
import { DebugModeBanner } from "./mode/DebugModeBanner";
Component.displayName = "DashboardPage";

const GRID_GAP_PX = 16;

type LayoutPreview = {
  widgetId: string;
  layout: WidgetLayout;
  valid: boolean;
};

export function Component() {
  const { buildMode } = useApps();
  const app = useCurrentApp();
  const navigate = useNavigate();
  const sensors = useSensors(useSensor(MouseSensor));
  const gridRef = useRef<HTMLDivElement>(null);
  const [preview, setPreview] = useState<LayoutPreview | null>(null);
  const [isDragging, setIsDragging] = useState(false);

  if (!app) return <Navigate to="/" />;
  if (app.lockReason) {
    return (
      <TeaserDashboardContainer app={app}>
        <AppLockedContent reason={app.lockReason} />
      </TeaserDashboardContainer>
    );
  }

  if (!app.hasEvents) return <OnboardingDashboard app={app} />;

  const getWidgetsForApp = useAtomValue(getDashboardWidgetsForAppAtom);
  const widgetsConfig = getWidgetsForApp(app.id);
  const setWidgetsConfig = useSetAtom(dashboardWidgetsAtom);
  const orderedWidgets = useMemo(() => sortByPosition(widgetsConfig), [widgetsConfig]);
  const freeRow = nextFreeRow(widgetsConfig);

  const resetFilters = () => navigate(`/${app.id}/`);

  const toggleMinimize = (widgetId: string) => {
    setWidgetsConfig({
      type: "toggle-minimized",
      widgetId,
      appId: app.id,
    });
  };

  const toGridColumn = (clientX: number): number => {
    const rect = gridRef.current!.getBoundingClientRect();
    const columnWidth = (rect.width - (GRID_COLUMNS - 1) * GRID_GAP_PX) / GRID_COLUMNS;
    return (clientX - rect.left + GRID_GAP_PX / 2) / (columnWidth + GRID_GAP_PX);
  };

  // Rows have different heights, so the row under the pointer is looked up from the rendered cells
  const toGridRow = (clientY: number): number => {
    const bounds = new Map<number, { top: number; bottom: number }>();
    gridRef.current!.querySelectorAll<HTMLElement>("[data-grid-row]").forEach((cell) => {
      const row = Number(cell.dataset.gridRow);
      const rect = cell.getBoundingClientRect();
      const current = bounds.get(row);
      bounds.set(row, {
        top: Math.min(current?.top ?? rect.top, rect.top),
        bottom: Math.max(current?.bottom ?? rect.bottom, rect.bottom),
      });
    });

    let closest = 0;
    let closestDistance = Number.POSITIVE_INFINITY;
    bounds.forEach(({ top, bottom }, row) => {
      const distance = clientY < top ? top - clientY : clientY > bottom ? clientY - bottom : 0;
      if (distance < closestDistance) {
        closest = row;
        closestDistance = distance;
      }
    });
    return closest;
  };

  const handleDragMove = (event: DragMoveEvent) => {
    const widget = widgetsConfig.find((w) => w.id === event.active.id);
    const start = getEventCoordinates(event.activatorEvent);
    const translated = event.active.rect.current.translated;
    if (!widget || !start || !translated) return;

    const row = toGridRow(start.y + event.delta.y);
    const { layout, valid } = moveLayout(widgetsConfig, widget.id, widget.layout, row, toGridColumn(translated.left));
    setPreview({ widgetId: widget.id, layout, valid });
  };

  const handleDragEnd = () => {
    if (preview?.valid) {
      setWidgetsConfig({
        type: "move",
        widgetId: preview.widgetId,
        row: preview.layout.row,
        col: preview.layout.col,
        appId: app.id,
      });
    }
    setPreview(null);
    setIsDragging(false);
  };

  const handleResize = (widget: SingleWidgetConfig, edge: ResizeEdge, clientX: number) => {
    const layout = resizeLayout(widgetsConfig, widget.id, widget.layout, edge, toGridColumn(clientX));
    setPreview({ widgetId: widget.id, layout, valid: true });
  };

  const handleResizeEnd = () => {
    if (preview) {
      setWidgetsConfig({
        type: "resize",
        widgetId: preview.widgetId,
        col: preview.layout.col,
        width: preview.layout.width,
        appId: app.id,
      });
    }
    setPreview(null);
  };

  const removeWidget = (widgetId: string) => {
    setWidgetsConfig({
      type: "toggle-is-defined",
      widgetId,
      appId: app.id,
    });
  };

  const renderWidget = (widget: SingleWidgetConfig) => {
    const props = { appId: app.id, appName: app.name };
    const containerProps = {
      widgetConfig: widget,
      onToggleMinimize: () => toggleMinimize(widget.id),
      onResize: (edge: ResizeEdge, clientX: number) => handleResize(widget, edge, clientX),
      onResizeEnd: handleResizeEnd,
    };

    switch (widget.type) {
      case "custom-events-chart":
        return (
          <WidgetContainer
            {...containerProps}
            widgetName={widget?.title ?? "Custom Chart"}
            onRemove={() => removeWidget(widget.id)}
          >
            <EventsChartWidget {...props} widgetConfig={widget} />
          </WidgetContainer>
        );
      case "custom-property-chart":
        return (
          <WidgetContainer
            {...containerProps}
            widgetName={widget?.title ?? "Custom Chart"}
            onRemove={() => removeWidget(widget.id)}
          >
            <PropertyChartWidget {...props} widgetConfig={widget} />
          </WidgetContainer>
        );
      case "events-chart":
        return (
          <WidgetContainer {...containerProps} widgetName={widget?.title ?? "Events Chart"}>
            <MainChartWidget {...props} />
          </WidgetContainer>
        );
      case "countries":
        return (
          <LazyLoad className="h-full">
            <WidgetContainer {...containerProps} widgetName={widget?.title ?? "Countries"}>
              <CountryWidget {...props} />
            </WidgetContainer>
          </LazyLoad>
        );
      case "operating-systems":
        return (
          <LazyLoad className="h-full">
            <WidgetContainer {...containerProps} widgetName={widget?.title ?? "Operating Systems"}>
              <OSWidget {...props} />
            </WidgetContainer>
          </LazyLoad>
        );
      case "events":
        return (
          <LazyLoad className="h-full">
            <WidgetContainer {...containerProps} widgetName={widget?.title ?? "Events"}>
              <EventWidget {...props} />
            </WidgetContainer>
          </LazyLoad>
        );
      case "app-versions":
        return (
          <LazyLoad className="h-full">
            <WidgetContainer {...containerProps} widgetName={widget?.title ?? "App Versions"}>
              <VersionWidget {...props} />
            </WidgetContainer>
          </LazyLoad>
        );
      default:
        return null;
    }
  };

  const aside = () => {
    if (app.hasOwnership) return null;
    return (
      <TooltipProvider delayDuration={0}>
        <Tooltip>
          <TooltipTrigger>
            <IconShare className="h-4 w-4 text-muted-foreground" />
          </TooltipTrigger>
          <TooltipContent>
            <LazyLoad key="shared-with-me">
              <AppShareInfo appId={app.id} />
            </LazyLoad>
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>
    );
  };

  return (
    <Page title={app.name}>
      {buildMode === "debug" && <DebugModeBanner />}
      <div className="space-y-4">
        <div className="flex justify-between items-center">
          <PageHeading title="Dashboard" aside={aside()} onClick={resetFilters} />
          <div className="flex items-end space-x-2">
            <BuildModeSelector />
            <DateFilterContainer />
          </div>
        </div>
        <div className="flex w-full justify-end">
          <CurrentFilters />
        </div>
        <DndContext
          sensors={sensors}
          onDragStart={() => setIsDragging(true)}
          onDragMove={handleDragMove}
          onDragEnd={handleDragEnd}
          onDragCancel={handleDragEnd}
        >
          <div ref={gridRef} className="grid grid-cols-1 md:grid-cols-6 gap-4">
            {orderedWidgets.map((widget) => (
              <GridCell key={widget.id} layout={widget.layout}>
                {renderWidget(widget)}
              </GridCell>
            ))}
            {/* An empty row at the bottom while moving, so widgets can be put on a new row */}
            {isDragging && (
              <GridCell layout={{ row: freeRow, col: 0, width: 6 }}>
                <div className="h-24 rounded-lg border border-dashed border-border" />
              </GridCell>
            )}
            {preview && (
              <GridCell layout={preview.layout} isPreview className="pointer-events-none z-30">
                <div
                  className={`h-full min-h-[6rem] rounded-lg border-2 border-dashed ${
                    preview.valid ? "border-primary bg-primary/10" : "border-destructive"
                  }`}
                />
              </GridCell>
            )}
          </div>
        </DndContext>
      </div>
    </Page>
  );
}

function GridCell(props: { layout: WidgetLayout; isPreview?: boolean; className?: string; children: ReactNode }) {
  const style = {
    "--grid-row": props.layout.row + 1,
    "--grid-col": props.layout.col + 1,
    "--grid-span": props.layout.width,
  } as CSSProperties;

  return (
    <div
      data-grid-row={props.isPreview ? undefined : props.layout.row}
      style={style}
      className={`min-w-0 md:[grid-row:var(--grid-row)] md:[grid-column:var(--grid-col)_/_span_var(--grid-span)] ${
        props.className ?? ""
      }`}
    >
      {props.children}
    </div>
  );
}
