import { endOfDay, subMinutes } from "date-fns";
import { useAtomValue } from "jotai";
import { atomWithStorage } from "jotai/utils";
import { createContext, useContext, useMemo } from "react";
import { dateFilterValuesAtom } from "../../../atoms/date-atoms";

// "real" buckets events by the actual moment they happened (shown in the viewer's time zone),
// "local" by the wall-clock time of the player who sent them.
export type TimeMode = "real" | "local";

export const timeModeAtom = atomWithStorage<TimeMode>("timemode", "real");

// Only the dashboard supports the local time mode. Components shared with other pages
// (like TopNDataContainer) only switch to local time when rendered inside this scope.
export const LocalTimeScope = createContext(false);

// Re-encodes a date's wall-clock time in the browser's time zone as the same wall-clock time in UTC,
// e.g. midnight on Sep 30 in Amsterdam becomes 2026-09-30T00:00:00Z. The server compares these
// against the players' local timestamps, which are stored the same way.
function toWallClockIso(iso: string): string {
  if (!iso) return iso;

  const d = new Date(iso);
  return new Date(
    Date.UTC(
      d.getFullYear(),
      d.getMonth(),
      d.getDate(),
      d.getHours(),
      d.getMinutes(),
      d.getSeconds(),
      d.getMilliseconds()
    )
  ).toISOString();
}

// A range that ends now (like "Today" or "Last 24 hours") runs until the end of the day in local time,
// otherwise players ahead of the viewer's time zone would be cut off at the viewer's current time.
function localEndDateIso(iso: string): string {
  if (!iso) return iso;

  const end = new Date(iso);
  return end >= subMinutes(new Date(), 1) ? endOfDay(end).toISOString() : iso;
}

// The dashboard's date filters, with the range converted to wall-clock time in local time mode.
// isAllTime is kept separately since the converted start no longer matches new Date(0).
export function useDateFilters() {
  const values = useAtomValue(dateFilterValuesAtom);
  const timeMode = useAtomValue(timeModeAtom);
  const inScope = useContext(LocalTimeScope);
  const localTime = inScope && timeMode === "local";

  return useMemo(() => {
    const isAllTime = values.startDateIso === new Date(0).toISOString();
    if (!localTime) return { ...values, isAllTime, localTime };

    return {
      ...values,
      startDateIso: toWallClockIso(values.startDateIso),
      endDateIso: toWallClockIso(localEndDateIso(values.endDateIso)),
      isAllTime,
      localTime,
    };
  }, [values, localTime]);
}
