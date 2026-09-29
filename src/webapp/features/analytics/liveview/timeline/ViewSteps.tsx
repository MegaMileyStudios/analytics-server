import { formatDate, formatTime } from "@fns/format-date";
import { formatNumber } from "@fns/format-number";
import { IconChevronDown, IconChevronRight, IconLayoutDashboard, IconSquarePlus, IconSquareX } from "@tabler/icons-react";
import { ReactNode, useState } from "react";
import { twMerge } from "tailwind-merge";
import { TimelineBlockNode, TimelineCloseNode } from "./build-session-tree";

const styles = {
  main: {
    icon: "text-blue-600 bg-blue-100 border-blue-300 dark:text-blue-400 dark:bg-blue-950 dark:border-blue-800",
    guide: "border-blue-300 dark:border-blue-800",
    label: "Main view",
  },
  popup: {
    icon: "text-green-600 bg-green-100 border-green-300 dark:text-green-400 dark:bg-green-950 dark:border-green-800",
    guide: "border-green-300 dark:border-green-800",
    label: "Popup opened",
  },
  close: {
    icon: "text-amber-600 bg-amber-100 border-amber-300 dark:text-amber-400 dark:bg-amber-950 dark:border-amber-800",
    label: "Popup closed",
  },
  impliedClose: {
    icon: "text-red-600 bg-red-100 border-red-300 dark:text-red-400 dark:bg-red-950 dark:border-red-800",
    label: "Closed (implied — no PopupClose received)",
  },
};

function formatSeconds(ms: number): string {
  const seconds = ms / 1000;
  if (seconds > 0 && seconds < 1) return `${seconds.toFixed(1)}s`;
  return formatNumber(Math.round(seconds), "duration");
}

function relativeTime(timestamp: Date, distance: number, isFirst: boolean): string {
  if (isFirst || distance > 60000) return `${formatDate(timestamp)} ${formatTime(timestamp)}`;
  if (distance > 0) return `${formatSeconds(distance)} later`;
  return "";
}

type BlockProps = {
  node: TimelineBlockNode;
  children: ReactNode;
};

export function ViewBlockStep(props: BlockProps) {
  const [expanded, setExpanded] = useState(true);
  const { node } = props;
  const style = styles[node.kind];
  const Icon = node.kind === "main" ? IconLayoutDashboard : IconSquarePlus;
  const Chevron = expanded ? IconChevronDown : IconChevronRight;
  const hasChildren = node.children.length > 0;

  return (
    <div className="relative">
      <button
        type="button"
        className="relative flex items-center space-x-2 text-left disabled:cursor-default"
        onClick={() => setExpanded((e) => !e)}
        disabled={!hasChildren}
        aria-expanded={expanded}
      >
        <Icon className={twMerge("p-1.5 h-8 w-8 border rounded-full shrink-0", style.icon)} />
        <div>
          <div className="text-xs text-muted-foreground" title={`${formatDate(node.timestamp)} ${formatTime(node.timestamp)}`}>
            {[style.label, relativeTime(node.timestamp, node.distance, node.isFirst)].filter(Boolean).join(" · ")}
          </div>
          <div className="flex items-center gap-2">
            <span className="text-lg">{node.name}</span>
            <span className="text-xs text-muted-foreground tabular-nums">
              {node.endedAt ? formatSeconds(node.endedAt.getTime() - node.timestamp.getTime()) : "still open"}
            </span>
            {hasChildren && <Chevron className="h-4 w-4 text-muted-foreground" />}
          </div>
        </div>
      </button>

      {hasChildren && expanded && (
        <div className={twMerge("ml-4 mt-4 pl-6 border-l-2 flex flex-col gap-4", style.guide)}>{props.children}</div>
      )}
    </div>
  );
}

export function ViewCloseStep(props: { node: TimelineCloseNode }) {
  const { node } = props;
  const style = node.implied ? styles.impliedClose : styles.close;

  return (
    <div className="relative">
      <div className="relative flex items-center space-x-2">
        <IconSquareX className={twMerge("p-1.5 h-8 w-8 border rounded-full shrink-0", style.icon)} />
        <div>
          <div className="text-xs text-muted-foreground" title={`${formatDate(node.timestamp)} ${formatTime(node.timestamp)}`}>
            {[
              node.stray ? "Popup closed (it wasn't open)" : style.label,
              node.implied ? "" : relativeTime(node.timestamp, node.distance, false),
            ]
              .filter(Boolean)
              .join(" · ")}
          </div>
          <span className="text-lg">{node.name}</span>
        </div>
      </div>
    </div>
  );
}
