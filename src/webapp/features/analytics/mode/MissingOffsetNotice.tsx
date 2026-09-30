import { useApps } from "@features/apps";
import { formatNumber } from "@fns/format-number";
import { useQuery } from "@tanstack/react-query";
import { useAtomValue } from "jotai";
import { useSearchParams } from "react-router-dom";
import { dateFilterValuesAtom } from "../../../atoms/date-atoms";
import { missingTimezoneOffset } from "../query";
import { timeModeAtom } from "./time-mode";

type Props = {
  appId: string;
};

export function MissingOffsetNotice(props: Props) {
  const { buildMode } = useApps();
  const [searchParams] = useSearchParams();
  const timeMode = useAtomValue(timeModeAtom);
  const { startDateIso, endDateIso } = useAtomValue(dateFilterValuesAtom);

  const countryCode = searchParams.get("countryCode") || "";
  const appVersion = searchParams.get("appVersion") || "";
  const eventName = searchParams.get("eventName") || "";
  const osName = searchParams.get("osName") || "";

  const { data } = useQuery({
    queryKey: [
      "missing-timezone-offset",
      buildMode,
      props.appId,
      startDateIso,
      endDateIso,
      countryCode,
      appVersion,
      eventName,
      osName,
    ],
    queryFn: () =>
      missingTimezoneOffset({
        buildMode,
        appId: props.appId,
        startDate: startDateIso,
        endDate: endDateIso,
        countryCode,
        appVersion,
        eventName,
        osName,
      }),
    staleTime: 10000,
    enabled: timeMode === "local" && !!startDateIso && !!endDateIso,
  });

  if (timeMode !== "local" || !data?.events) return null;

  return (
    <p className="text-xs text-muted-foreground">
      {formatNumber(data.events)} {data.events === 1 ? "event" : "events"} without a timezone offset{" "}
      {data.events === 1 ? "is" : "are"} hidden in local time.
    </p>
  );
}
