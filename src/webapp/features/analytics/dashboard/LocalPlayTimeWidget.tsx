import { Button } from "@components/Button";
import { Chart } from "@components/Chart";
import { EmptyState } from "@components/EmptyState";
import { ErrorState } from "@components/ErrorState";
import { LoadingState } from "@components/LoadingState";
import { Popover, PopoverContent, PopoverTrigger } from "@components/Popover";
import { ToggleGroup, ToggleGroupList, ToggleGroupTrigger } from "@components/ToggleGroup";
import { useApps } from "@features/apps";
import { hourCycle } from "@features/env";
import { useChartColors } from "@features/theme";
import { formatNumber } from "@fns/format-number";
import { IconMenu2 } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { ChartConfiguration } from "chart.js";
import { useSetAtom } from "jotai";
import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { dashboardWidgetsAtom, LocalPlayTimeWidgetConfig, SingleWidgetConfig } from "../../../atoms/widgets-atoms";
import { useDateFilters } from "../mode/time-mode";
import { LocalPlayTimeCell, LocalPlayTimeMetric, localPlayTime } from "../query";

type Props = {
  appId: string;
  appName: string;
  widgetConfig: SingleWidgetConfig<LocalPlayTimeWidgetConfig>;
};

const HOURS = Array.from({ length: 24 }, (_, hour) => hour);
const DAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

const HEATMAP_RGB = "96, 165, 250";

const viewerTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";

function formatHour(hour: number): string {
  if (hourCycle === "h12") {
    const suffix = hour < 12 ? "am" : "pm";
    return `${hour % 12 === 0 ? 12 : hour % 12}${suffix}`;
  }
  return `${hour.toString().padStart(2, "0")}:00`;
}

function metricLabel(metric: LocalPlayTimeMetric, value: number): string {
  const name = metric === "sessions" ? "session" : "event";
  return `${formatNumber(value)} ${value === 1 ? name : `${name}s`}`;
}

export function LocalPlayTimeWidget(props: Props) {
  const { buildMode } = useApps();
  const [searchParams] = useSearchParams();
  const setWidgetsConfig = useSetAtom(dashboardWidgetsAtom);
  const { startDateIso, endDateIso, localTime } = useDateFilters();

  const config = props.widgetConfig.properties ?? {};
  const display = config.display ?? "histogram";
  const metric = config.metric ?? "sessions";

  const countryCode = searchParams.get("countryCode") || "";
  const appVersion = searchParams.get("appVersion") || "";
  const eventName = searchParams.get("eventName") || "";
  const osName = searchParams.get("osName") || "";

  const update = (properties: LocalPlayTimeWidgetConfig) => {
    setWidgetsConfig({
      widgetId: props.widgetConfig.id,
      type: "update-properties",
      properties: { ...config, ...properties },
      appId: props.appId,
    });
  };

  const { isLoading, isError, data, refetch } = useQuery({
    queryKey: [
      "local-play-time",
      buildMode,
      props.appId,
      startDateIso,
      endDateIso,
      localTime,
      metric,
      countryCode,
      appVersion,
      eventName,
      osName,
    ],
    queryFn: () =>
      localPlayTime({
        buildMode,
        appId: props.appId,
        startDate: startDateIso,
        endDate: endDateIso,
        countryCode,
        appVersion,
        eventName,
        osName,
        localTime,
        metric,
        timeZone: viewerTimeZone,
      }),
    staleTime: 10000,
    enabled: !!startDateIso && !!endDateIso,
  });

  const cells = data ?? [];
  const isEmpty = cells.every((x) => x.value === 0);

  return (
    <>
      <div className="flex justify-between items-center gap-2 pb-3">
        <div className="min-w-0">
          <p className="font-medium">Local Play Time</p>
          <p className="text-xs text-muted-foreground truncate">
            {localTime ? "In each player's local time" : `In your time zone (${viewerTimeZone})`}
          </p>
        </div>
        <Popover>
          <PopoverTrigger asChild>
            <Button variant="ghost" size="xs" className="h-7 w-7 p-0" aria-label="Chart settings">
              <IconMenu2 className="h-4 w-4" />
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-auto max-w-[90vw]">
            <div className="flex flex-col gap-3">
              <ToggleGroup
                value={display}
                onValueChange={(value) => update({ display: value as "histogram" | "heatmap" })}
              >
                <ToggleGroupList>
                  <ToggleGroupTrigger value="histogram">Histogram</ToggleGroupTrigger>
                  <ToggleGroupTrigger value="heatmap">Heatmap</ToggleGroupTrigger>
                </ToggleGroupList>
              </ToggleGroup>
              <ToggleGroup value={metric} onValueChange={(value) => update({ metric: value as LocalPlayTimeMetric })}>
                <ToggleGroupList>
                  <ToggleGroupTrigger value="sessions">Sessions</ToggleGroupTrigger>
                  <ToggleGroupTrigger value="events">Events</ToggleGroupTrigger>
                </ToggleGroupList>
              </ToggleGroup>
              {metric === "sessions" && (
                <p className="text-xs text-muted-foreground max-w-[16rem]">
                  Sessions are counted at the time of their first event.
                </p>
              )}
            </div>
          </PopoverContent>
        </Popover>
      </div>
      <div className="h-60 md:h-80 w-full">
        {isError ? (
          <ErrorState refetch={refetch} />
        ) : isLoading ? (
          <LoadingState />
        ) : isEmpty ? (
          <EmptyState />
        ) : display === "heatmap" ? (
          <PlayTimeHeatmap cells={cells} metric={metric} />
        ) : (
          <PlayTimeHistogram cells={cells} metric={metric} />
        )}
      </div>
    </>
  );
}

function PlayTimeHistogram(props: { cells: LocalPlayTimeCell[]; metric: LocalPlayTimeMetric }) {
  const colors = useChartColors();

  const config = useMemo((): ChartConfiguration => {
    const perHour = HOURS.map(() => 0);
    props.cells.forEach((cell) => (perHour[cell.hour] += cell.value));
    const total = perHour.reduce((sum, x) => sum + x, 0);

    return {
      type: "bar",
      data: {
        labels: HOURS.map(formatHour),
        datasets: [{ data: perHour, backgroundColor: colors.primary, borderRadius: 4, maxBarThickness: 48 }],
      },
      options: {
        animation: false,
        maintainAspectRatio: false,
        scales: {
          x: {
            grid: { display: false },
            border: { display: false },
            ticks: { autoSkip: true, maxRotation: 0, autoSkipPadding: 12 },
          },
          y: {
            beginAtZero: true,
            grid: { tickWidth: 0 },
            ticks: { maxTicksLimit: 4 },
            border: { display: false, dash: [4, 4] },
          },
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const value = ctx.parsed.y;
                const share = total ? ` (${Math.round((value / total) * 1000) / 10}%)` : "";
                return `${metricLabel(props.metric, value)}${share}`;
              },
            },
          },
        },
      },
    };
  }, [props.cells, props.metric, colors]);

  return <Chart config={config} />;
}

function PlayTimeHeatmap(props: { cells: LocalPlayTimeCell[]; metric: LocalPlayTimeMetric }) {
  const values = new Map(props.cells.map((cell) => [`${cell.dayOfWeek}-${cell.hour}`, cell.value]));
  const max = Math.max(1, ...props.cells.map((cell) => cell.value));

  return (
    <div className="h-full w-full overflow-x-auto">
      <div
        className="grid h-full min-w-[36rem] gap-0.5 text-xs text-muted-foreground"
        style={{ gridTemplateColumns: "2.5rem repeat(24, minmax(0, 1fr))", gridTemplateRows: "repeat(7, 1fr) auto" }}
      >
        {DAYS.map((day, dayIndex) => (
          <div key={day} className="contents">
            <div className="flex items-center">{day}</div>
            {HOURS.map((hour) => {
              const value = values.get(`${dayIndex + 1}-${hour}`) ?? 0;
              // Keep a faint cell for zero so the grid stays visible
              const alpha = value === 0 ? 0.04 : 0.15 + 0.85 * (value / max);
              return (
                <div
                  key={hour}
                  className="rounded-sm"
                  style={{ backgroundColor: `rgba(${HEATMAP_RGB}, ${alpha})` }}
                  title={`${day} ${formatHour(hour)}: ${metricLabel(props.metric, value)}`}
                />
              );
            })}
          </div>
        ))}
        <div />
        {HOURS.map((hour) => (
          <div key={hour} className="text-center pt-1">
            {hour % 3 === 0 ? formatHour(hour) : ""}
          </div>
        ))}
      </div>
    </div>
  );
}
