import { LineChart } from "@features/analytics/dashboard/LineChart";
import { useChartColors } from "@features/theme";
import { QueryObserverResult, RefetchOptions } from "@tanstack/react-query";
import { useMemo } from "react";
import { PeriodicStats } from "../query";

type ActiveMetric = "users" | "sessions" | "events";

export type MetricsBreakdown = Record<ActiveMetric, { persistent: number[]; inferred: number[] }>;

type Props = {
  hasPartialData: boolean;
  labels: string[];
  activeMetric: ActiveMetric;
  users: number[];
  sessions: number[];
  events: number[];
  breakdown?: MetricsBreakdown;
  granularity: "hour" | "day" | "month";
  isEmpty?: boolean;
  isLoading?: boolean;
  isError?: boolean;
  formatLabel?: (label: string | number) => string;
  renderTooltip?: (dataPoint: TooltipDataPoint) => JSX.Element;
  refetch?: (options?: RefetchOptions | undefined) => Promise<QueryObserverResult<PeriodicStats, Error>>;
};

type TooltipDataPoint = {
  label: string;
  points: Array<{
    name: string;
    value: number;
  }>;
};

const labels = {
  "sessions-hour": "Sessions",
  "sessions-day": "Sessions",
  "sessions-month": "Sessions",
  "events-hour": "Events",
  "events-day": "Events",
  "events-month": "Events",
  "users-hour": "Users",
  "users-day": "Users",
  "users-month": "Daily Users",
};

export function MetricsChart(props: Props) {
  const colors = useChartColors();
  const label = labels[`${props.activeMetric}-${props.granularity}`] ?? "";

  const breakdown = props.breakdown?.[props.activeMetric];

  const datasets = useMemo(() => {
    const total = {
      label,
      data: props[props.activeMetric],
      hasPartialData: props.hasPartialData,
      color: colors.primary,
    };

    // Without any persistent/inferred data (all zeros) there's nothing to split
    const hasBreakdown = !!breakdown && [...breakdown.persistent, ...breakdown.inferred].some((x) => x > 0);
    if (!hasBreakdown) return [total];

    return [
      total,
      {
        label: `Persistent ${label.toLowerCase()}`,
        data: breakdown.persistent,
        hasPartialData: props.hasPartialData,
        color: colors.persistent,
        fill: false,
      },
      {
        label: `Inferred ${label.toLowerCase()}`,
        data: breakdown.inferred,
        hasPartialData: props.hasPartialData,
        color: colors.inferred,
        fill: false,
      },
    ];
  }, [props.activeMetric, props.hasPartialData, props[props.activeMetric], breakdown, colors, label]);

  return (
    <LineChart
      labels={props.labels}
      datasets={datasets}
      granularity={props.granularity}
      isEmpty={props.isEmpty}
      isLoading={props.isLoading}
      isError={props.isError}
      formatLabel={props.formatLabel}
      renderTooltip={props.renderTooltip}
      refetch={props.refetch}
    />
  );
}
