import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@components/Tooltip";
import { format, parseISO } from "date-fns";
import { twMerge } from "tailwind-merge";
import { CohortGranularity, RetentionCohorts } from "../query";

type Props = {
  data: RetentionCohorts;
  granularity: CohortGranularity;
};

const prefixes: Record<CohortGranularity, string> = { day: "D", week: "W", month: "M" };

function formatCohort(cohortDate: string, granularity: CohortGranularity) {
  const date = parseISO(cohortDate.slice(0, 10));
  if (granularity === "month") return format(date, "MMM yyyy");
  if (granularity === "week") return `Week of ${format(date, "MMM d")}`;
  return format(date, "EEE, MMM d");
}

function Cell(props: { retained: number | null; size: number }) {
  if (props.retained === null || props.size === 0) return <div className="w-14 h-9" />;

  const share = props.retained / props.size;
  // Primary color (Tailwind blue-500) with the opacity scaled by the share, so it works on light and dark backgrounds
  const alpha = 0.08 + share * 0.82;

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <div
          className={twMerge(
            "w-14 h-9 rounded flex items-center justify-center tabular-nums cursor-default",
            alpha >= 0.5 ? "text-white" : "text-foreground"
          )}
          style={{ backgroundColor: `rgba(59, 130, 246, ${alpha})` }}
        >
          {Math.round(share * 100)}%
        </div>
      </TooltipTrigger>
      <TooltipContent>
        {props.retained.toLocaleString()} of {props.size.toLocaleString()} players
      </TooltipContent>
    </Tooltip>
  );
}

export function RetentionTable(props: Props) {
  const { cohorts, periods } = props.data;
  const columns = Array.from({ length: periods + 1 }, (_, n) => n);

  // Weighted average over the cohorts that have reached period N
  const average = columns.map((n) => {
    const reached = cohorts.filter((c) => c.retained[n] !== null);
    return {
      retained: reached.reduce((sum, c) => sum + (c.retained[n] ?? 0), 0),
      size: reached.reduce((sum, c) => sum + c.size, 0),
    };
  });
  const totalPlayers = cohorts.reduce((sum, c) => sum + c.size, 0);

  const stickyCell = "sticky left-0 z-10 bg-background pr-4 text-left whitespace-nowrap";

  return (
    <TooltipProvider delayDuration={0}>
      <div className="overflow-x-auto rounded-md border p-2">
        <table className="text-sm border-separate border-spacing-0.5">
          <thead>
            <tr className="text-muted-foreground">
              <th className={twMerge(stickyCell, "font-medium")}>Cohort</th>
              <th className="font-medium text-right pr-3">Players</th>
              {columns.map((n) => (
                <th key={n} className="font-medium w-14">
                  {prefixes[props.granularity]}
                  {n}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            <tr className="font-medium">
              <td className={stickyCell}>Average</td>
              <td className="text-right pr-3 tabular-nums">{totalPlayers.toLocaleString()}</td>
              {average.map((cell, n) => (
                <td key={n}>
                  <Cell retained={cell.size === 0 ? null : cell.retained} size={cell.size} />
                </td>
              ))}
            </tr>
            {cohorts.map((cohort) => (
              <tr key={cohort.cohortDate}>
                <td className={stickyCell}>{formatCohort(cohort.cohortDate, props.granularity)}</td>
                <td className="text-right pr-3 tabular-nums">{cohort.size.toLocaleString()}</td>
                {cohort.retained.map((retained, n) => (
                  <td key={n}>
                    <Cell retained={retained} size={cohort.size} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </TooltipProvider>
  );
}
