import { Chart } from "@components/Chart";
import { EmptyState } from "@components/EmptyState";
import { ErrorState } from "@components/ErrorState";
import { LoadingState } from "@components/LoadingState";
import { EventPropDistributionItem } from "@features/analytics/query";
import { useChartColors } from "@features/theme";
import { formatNumber } from "@fns/format-number";
import { QueryObserverResult, RefetchOptions } from "@tanstack/react-query";
import { ChartConfiguration } from "chart.js";
import { useMemo } from "react";

export type CategoryDisplay = "bar" | "bar-time" | "pie";

type Props = {
  display: CategoryDisplay;
  items: EventPropDistributionItem[];
  isLoading?: boolean;
  isError?: boolean;
  refetch?: (options?: RefetchOptions | undefined) => Promise<QueryObserverResult<unknown, Error>>;
};

// Gray for the "Other" and "Undefined" buckets so they don't look like a real value
const BUCKET_COLOR = "#94A3B8";

export function categoryColors(items: { isOther: boolean; isUndefined: boolean }[], palette: string[]): string[] {
  let colorIndex = 0;
  return items.map((x) => (x.isOther || x.isUndefined ? BUCKET_COLOR : palette[colorIndex++ % palette.length]));
}

export function CategoryChart(props: Props) {
  const colors = useChartColors();

  const config = useMemo((): ChartConfiguration => {
    const labels = props.items.map((x) => x.name);
    const data = props.items.map((x) => x.value);
    const total = data.reduce((sum, x) => sum + x, 0);
    const backgroundColor = categoryColors(props.items, colors.series);
    const tooltipLabel = (value: number) =>
      `${formatNumber(value)} events (${total ? Math.round((value / total) * 1000) / 10 : 0}%)`;

    if (props.display === "pie") {
      return {
        type: "pie",
        data: { labels, datasets: [{ data, backgroundColor, borderColor: colors.background, borderWidth: 2 }] },
        options: {
          animation: false,
          maintainAspectRatio: false,
          plugins: {
            legend: {
              position: "right",
              labels: { usePointStyle: true, pointStyle: "circle", boxWidth: 8, boxHeight: 8, padding: 12 },
            },
            tooltip: { callbacks: { label: (ctx) => tooltipLabel(ctx.parsed) } },
          },
        },
      };
    }

    return {
      type: "bar",
      data: { labels, datasets: [{ data, backgroundColor, borderRadius: 4, maxBarThickness: 48 }] },
      options: {
        animation: false,
        maintainAspectRatio: false,
        scales: {
          x: { grid: { display: false }, border: { display: false } },
          y: {
            beginAtZero: true,
            grid: { tickWidth: 0 },
            ticks: { maxTicksLimit: 4 },
            border: { display: false, dash: [4, 4] },
          },
        },
        plugins: {
          legend: { display: false },
          tooltip: { callbacks: { label: (ctx) => tooltipLabel(ctx.parsed.y) } },
        },
      },
    };
  }, [props.items, props.display, colors]);

  const isEmpty = props.items.every((x) => x.value === 0);

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
