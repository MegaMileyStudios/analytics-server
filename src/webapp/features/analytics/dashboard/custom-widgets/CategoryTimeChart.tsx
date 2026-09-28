import { Chart } from "@components/Chart";
import { EmptyState } from "@components/EmptyState";
import { ErrorState } from "@components/ErrorState";
import { LoadingState } from "@components/LoadingState";
import { StackMode } from "@features/analytics/dashboard/LineChart";
import { EventPropDistributionSeries, Granularity } from "@features/analytics/query";
import { useChartColors } from "@features/theme";
import { formatPeriod } from "@fns/format-date";
import { formatNumber } from "@fns/format-number";
import { QueryObserverResult, RefetchOptions } from "@tanstack/react-query";
import { ChartConfiguration } from "chart.js";
import { useMemo } from "react";
import { categoryColors } from "./CategoryChart";

type Props = {
  data?: EventPropDistributionSeries;
  stackMode: StackMode;
  granularity: Granularity;
  isLoading?: boolean;
  isError?: boolean;
  refetch?: (options?: RefetchOptions | undefined) => Promise<QueryObserverResult<unknown, Error>>;
};

export function CategoryTimeChart(props: Props) {
  const colors = useChartColors();
  const periods = props.data?.periods ?? [];
  const series = props.data?.series ?? [];

  const config = useMemo((): ChartConfiguration => {
    const isStacked = props.stackMode !== "none";
    const totals = periods.map((_, i) => series.reduce((sum, s) => sum + s.values[i], 0));
    const backgroundColor = categoryColors(series, colors.series);

    return {
      type: "bar",
      data: {
        labels: periods,
        datasets: series.map((s, index) => ({
          label: s.name,
          data:
            props.stackMode === "filled" ? s.values.map((v, i) => (totals[i] ? (v / totals[i]) * 100 : 0)) : s.values,
          backgroundColor: backgroundColor[index],
          borderRadius: isStacked ? 0 : 2,
          maxBarThickness: 48,
        })),
      },
      options: {
        animation: false,
        maintainAspectRatio: false,
        interaction: { mode: "index", intersect: false },
        scales: {
          x: {
            stacked: isStacked,
            grid: { display: false },
            border: { display: false },
            ticks: {
              autoSkip: true,
              maxRotation: 0,
              autoSkipPadding: 20,
              maxTicksLimit: 8,
              callback: function (value) {
                const label = typeof value === "number" ? this.getLabelForValue(value) : value;
                return formatPeriod(props.granularity, label.toString());
              },
            },
          },
          y: {
            stacked: isStacked,
            beginAtZero: true,
            max: props.stackMode === "filled" ? 100 : undefined,
            grid: { tickWidth: 0 },
            ticks: {
              maxTicksLimit: 4,
              callback: props.stackMode === "filled" ? (value) => `${value}%` : undefined,
            },
            border: { display: false, dash: [4, 4] },
          },
        },
        plugins: {
          legend: {
            position: "top",
            labels: { usePointStyle: true, pointStyle: "circle", boxWidth: 8, boxHeight: 8, padding: 12 },
          },
          tooltip: {
            callbacks: {
              title: (items) => formatPeriod(props.granularity, items[0]?.label ?? ""),
              label: (ctx) => {
                const value = series[ctx.datasetIndex].values[ctx.dataIndex];
                const total = totals[ctx.dataIndex];
                const share = total ? ` (${Math.round((value / total) * 1000) / 10}%)` : "";
                return `${ctx.dataset.label}: ${formatNumber(value)}${share}`;
              },
            },
          },
        },
      },
    };
  }, [props.data, props.stackMode, props.granularity, colors]);

  const isEmpty = series.every((s) => s.values.every((v) => v === 0));

  return (
    <div className="h-60 md:h-80 w-full">
      {props.isError ? (
        <ErrorState refetch={props.refetch} />
      ) : props.isLoading ? (
        <LoadingState />
      ) : isEmpty ? (
        <EmptyState />
      ) : (
        <Chart config={config} />
      )}
    </div>
  );
}
