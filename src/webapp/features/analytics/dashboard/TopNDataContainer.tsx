import { useApps } from "@features/apps";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { QueryParams, TopNItem, TopNQueryChildrenProps } from "../query";
import { useDateFilters } from "../mode/time-mode";

type Props = {
  appId: string;
  queryName: string;
  query: (params: QueryParams) => Promise<TopNItem[]>;
  children: (props: TopNQueryChildrenProps) => JSX.Element;
};

export function TopNDataContainer(props: Props) {
  const { buildMode } = useApps();
  const [searchParams] = useSearchParams();
  const { startDateIso, endDateIso, granularity, localTime } = useDateFilters();

  const countryCode = searchParams.get("countryCode") || "";
  const appVersion = searchParams.get("appVersion") || "";
  const eventName = searchParams.get("eventName") || "";
  const osName = searchParams.get("osName") || "";

  const { isLoading, isError, data, refetch } = useQuery({
    queryKey: [
      props.queryName,
      buildMode,
      props.appId,
      startDateIso,
      endDateIso,
      localTime,
      countryCode,
      appVersion,
      eventName,
      osName,
    ],
    queryFn: () =>
      props.query({
        buildMode,
        appId: props.appId,
        startDate: startDateIso,
        endDate: endDateIso,
        granularity,
        countryCode,
        appVersion,
        eventName,
        osName,
        localTime,
      }),
    staleTime: 10000,
    enabled: !!startDateIso && !!endDateIso && !!granularity,
  });

  return props.children({ isLoading, isError, items: data || [], refetch });
}
