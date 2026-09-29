import { EmptyState } from "@components/EmptyState";
import { ErrorState } from "@components/ErrorState";
import { formatNumber } from "@fns/format-number";
import { IconArrowDown, IconArrowUp } from "@tabler/icons-react";
import { QueryObserverResult, RefetchOptions } from "@tanstack/react-query";
import { useState } from "react";
import { twMerge } from "tailwind-merge";
import { TopNSkeleton } from "../dashboard/TopNSkeleton";
import { ViewStatsRow } from "../query";

type SortColumn = "name" | "visits" | "totalSeconds" | "avgSeconds" | "medianSeconds";

type Props = {
  title: React.ReactNode;
  nameLabel: string;
  rows: ViewStatsRow[];
  isLoading: boolean;
  isError: boolean;
  refetch?: (options?: RefetchOptions) => Promise<QueryObserverResult<ViewStatsRow[], Error>>;
  selected?: string;
  onSelect?: (name: string) => void;
};

const columns: { key: SortColumn; label: string }[] = [
  { key: "visits", label: "Visits" },
  { key: "totalSeconds", label: "Total" },
  { key: "avgSeconds", label: "Average" },
  { key: "medianSeconds", label: "Median" },
];

// formatNumber rounds everything under a second down to "0s", but popups are often shorter than that
export function formatViewDuration(seconds: number): string {
  if (seconds > 0 && seconds < 1) return `${seconds.toFixed(1)}s`;
  return formatNumber(Math.round(seconds), "duration");
}

export function ViewStatsTable(props: Props) {
  const [sort, setSort] = useState<{ column: SortColumn; desc: boolean }>({ column: "totalSeconds", desc: true });

  const toggleSort = (column: SortColumn) => {
    setSort((current) =>
      current.column === column ? { column, desc: !current.desc } : { column, desc: column !== "name" }
    );
  };

  const sorted = [...props.rows].sort((a, b) => {
    const result =
      sort.column === "name" ? a.name.localeCompare(b.name) : (a[sort.column] as number) - (b[sort.column] as number);
    return sort.desc ? -result : result;
  });

  const header = (column: SortColumn, label: string, className?: string) => (
    <th key={column} className={twMerge("px-2 py-2 text-sm font-medium text-muted-foreground", className)}>
      <button
        type="button"
        className={twMerge(
          "inline-flex items-center gap-1 hover:text-foreground",
          sort.column === column && "text-foreground"
        )}
        onClick={() => toggleSort(column)}
      >
        {label}
        {sort.column === column &&
          (sort.desc ? <IconArrowDown className="h-3.5 w-3.5" /> : <IconArrowUp className="h-3.5 w-3.5" />)}
      </button>
    </th>
  );

  const content = props.isError ? (
    <ErrorState refetch={props.refetch} />
  ) : props.isLoading ? (
    <TopNSkeleton />
  ) : props.rows.length === 0 ? (
    <EmptyState />
  ) : (
    <div className="overflow-x-auto max-h-[32rem] overflow-y-auto">
      <table className="min-w-full text-sm">
        <thead className="sticky top-0 bg-background">
          <tr>
            {header("name", props.nameLabel, "text-left")}
            {columns.map((c) => header(c.key, c.label, "text-right"))}
          </tr>
        </thead>
        <tbody>
          {sorted.map((row) => {
            const isSelected = props.selected === row.name;
            return (
              <tr
                key={row.name}
                onClick={props.onSelect ? () => props.onSelect!(row.name) : undefined}
                className={twMerge(
                  "border-t border-border",
                  props.onSelect && "cursor-pointer hover:bg-accent",
                  isSelected && "bg-primary-100 dark:bg-primary-900 hover:bg-primary-100 dark:hover:bg-primary-900"
                )}
              >
                <td className="px-2 py-2 font-medium break-all">{row.name}</td>
                <td className="px-2 py-2 text-right tabular-nums">{formatNumber(row.visits)}</td>
                <td className="px-2 py-2 text-right tabular-nums whitespace-nowrap">
                  {formatViewDuration(row.totalSeconds)}
                </td>
                <td className="px-2 py-2 text-right tabular-nums whitespace-nowrap">
                  {formatViewDuration(row.avgSeconds)}
                </td>
                <td className="px-2 py-2 text-right tabular-nums whitespace-nowrap">
                  {formatViewDuration(row.medianSeconds)}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );

  return (
    <div className="rounded-lg border border-border p-4 h-full min-w-0">
      <div className="font-medium mb-2">{props.title}</div>
      {content}
    </div>
  );
}
