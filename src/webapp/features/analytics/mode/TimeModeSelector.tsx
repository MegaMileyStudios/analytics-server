import { Popover, PopoverContent, PopoverTrigger } from "@components/Popover";
import { IconClock, IconWorld } from "@tabler/icons-react";
import { useAtom } from "jotai";
import { twJoin } from "tailwind-merge";
import { TimeMode, timeModeAtom } from "./time-mode";

const options = [
  { icon: IconClock, label: "Real time", value: "real" as TimeMode },
  { icon: IconWorld, label: "Local time", value: "local" as TimeMode },
];

export function TimeModeSelector() {
  const [timeMode, setTimeMode] = useAtom(timeModeAtom);

  return (
    <Popover>
      <PopoverTrigger className="relative" aria-label="Time mode">
        {timeMode === "real" ? (
          <IconClock className="h-5 w-5 mb-2" stroke="1" />
        ) : (
          <>
            <div className="absolute rounded-full bg-primary h-1.5 w-1.5 top-0 right-0" />
            <IconWorld className="h-5 w-5 mb-2" stroke="1" />
          </>
        )}
      </PopoverTrigger>
      <PopoverContent className="p-2 w-72">
        <div className="text-sm space-y-2 text-center">
          <p className="whitespace-nowrap">Which time should the charts use?</p>
          <div className="grid grid-cols-2">
            {options.map((option) => (
              <button
                key={option.value}
                value={option.value}
                onClick={() => setTimeMode(option.value)}
                className={twJoin(
                  "flex cursor-pointer items-center justify-center rounded py-2 focus-ring",
                  option.value === timeMode ? "bg-accent" : "text-muted-foreground"
                )}
              >
                <div className="flex items-center space-x-2">
                  <option.icon className="h-5 w-5" stroke="1.5" />
                  <span>{option.label}</span>
                </div>
              </button>
            ))}
          </div>
          <p className="text-muted-foreground text-xs text-center">
            Local time groups events and filters the date range by the time of day each player saw. Events sent without
            a timezone offset are left out.
          </p>
        </div>
      </PopoverContent>
    </Popover>
  );
}
