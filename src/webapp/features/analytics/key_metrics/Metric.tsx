import { GrowthIndicator } from "@components/GrowthIndicator";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@components/Tooltip";
import { formatNumber } from "@fns/format-number";
import { twJoin, twMerge } from "tailwind-merge";

type Props = {
  label: string;
  current: number;
  previous?: number;
  active?: boolean;
  activeClassName?: string;
  onClick?: VoidFunction;
  format: "duration" | "number";
  breakdown?: { persistent: number; inferred: number };
};

export function Metric(props: Props) {
  const Container = props.onClick ? "button" : "div";

  const TooltipOnOverflow = (
    <TooltipProvider delayDuration={0}>
      <Tooltip>
        <TooltipContent>{props.current}</TooltipContent>
        <TooltipTrigger>{formatNumber(props.current, props.format)}</TooltipTrigger>
      </Tooltip>
    </TooltipProvider>
  );

  // Without a persistent/inferred split (both zero) there's nothing to show
  const hasBreakdown = !!props.breakdown && (props.breakdown.persistent > 0 || props.breakdown.inferred > 0);

  const Breakdown = hasBreakdown && (
    <TooltipProvider delayDuration={0}>
      <Tooltip>
        <TooltipContent>Persistent / Inferred</TooltipContent>
        <TooltipTrigger asChild>
          <span className="ml-1 text-sm font-normal text-muted-foreground">
            ({formatNumber(props.breakdown?.persistent, props.format)}/
            {formatNumber(props.breakdown?.inferred, props.format)})
          </span>
        </TooltipTrigger>
      </Tooltip>
    </TooltipProvider>
  );

  return (
    <Container
      className={twJoin(
        "inline-flex flex-col text-center rounded p-2 min-w-[7rem]",
        props.onClick && "hover:bg-accent"
      )}
      onClick={props.onClick}
    >
      <div className="text-2xl font-semibold w-full">
        {props.format === "number" && props.current >= 1e3
          ? TooltipOnOverflow
          : formatNumber(props.current, props.format)}
        {Breakdown}
      </div>
      <div className="text-sm text-muted-foreground w-full">
        {props.label}{" "}
        <div className={twMerge("ml-1 p-1 inline-block rounded", props.activeClassName, !props.active && "hidden")} />
      </div>
      <GrowthIndicator
        className="mt-1 w-full"
        current={props.current}
        previous={props.previous}
        previousFormatted={`${formatNumber(props.previous ?? 0, props.format)} ${props.label.toLowerCase()}`}
      />
    </Container>
  );
}
