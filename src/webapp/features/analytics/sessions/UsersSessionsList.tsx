import { Button } from "@components/Button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@components/Select";
import { SessionsGridDisplay } from "@features/analytics/sessions/SessionsGridDisplay";
import { formatPersistentUserId } from "@fns/format-persistent-user-id";
import { IconChevronLeft, IconChevronRight, IconChevronsLeft, IconX } from "@tabler/icons-react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useAtom, useAtomValue } from "jotai";
import { useEffect, useMemo, useState } from "react";
import { QueryParams, historicalSessions } from "../query";
import { SESSIONS_PAGE_SIZES, sessionsCursorAtom, sessionsPageSizeAtom, sessionsUserIdAtom } from "./session-list-atoms";
import { UsersSessionsFilters } from "./UsersSessionsFilters";

type Props = {
  appId: string;
  buildMode: "release" | "debug";
};

type TopFilterValue = {
  eventName?: string;
  osName?: string;
  country?: string;
  appVersion?: string;
};

export function UserSessionsList(props: Props) {
  const [dateFilters, setDateFilters] = useAtom(sessionsCursorAtom);
  const [topFilters, setTopFilters] = useState<TopFilterValue>({});
  const persistentUserId = useAtomValue(sessionsUserIdAtom) || undefined;
  const [pageSizeValue, setPageSize] = useAtom(sessionsPageSizeAtom);
  const pageSize = SESSIONS_PAGE_SIZES.find((size) => size === Number(pageSizeValue)) ?? SESSIONS_PAGE_SIZES[0];

  const clearUserFilter = () => {
    setDateFilters({ userId: undefined });
  };

  const requestParams: QueryParams = useMemo(() => {
    const countryCode = !topFilters.country ? "" : topFilters.country;
    const appVersion = !topFilters.appVersion ? "" : topFilters.appVersion;
    const allParams: QueryParams = {
      appId: props.appId,
      buildMode: props.buildMode,
      startDate: dateFilters.startDate,
      endDate: dateFilters.endDate,
      sessionId: dateFilters.sessionId,
      eventName: topFilters.eventName,
      osName: topFilters.osName,
      countryCode,
      appVersion,
      persistentUserId,
      pageSize,
    };

    const params = Object.fromEntries(Object.entries(allParams).filter(([_, v]) => v != null));
    return params as QueryParams;
  }, [props, dateFilters, topFilters, persistentUserId, pageSize]);

  const { data, isSuccess, isPlaceholderData } = useQuery({
    queryKey: ["historical-sessions", props.appId, props.buildMode, dateFilters, topFilters, persistentUserId, pageSize],
    queryFn: () => historicalSessions(requestParams),
    placeholderData: keepPreviousData,
  });

  const sessions = [...(data || [])];
  if (dateFilters.startDate) {
    sessions.reverse();
  }

  // When we reach first page and hit previous one more time
  useEffect(() => {
    if (isSuccess && !isPlaceholderData && sessions.length === 0 && dateFilters.startDate) {
      setDateFilters({}, { replace: true });
    }
  }, [data, isSuccess, isPlaceholderData]);

  // Data for pager
  const firstSession = sessions[0];
  const lastSession = sessions[sessions.length - 1];
  const previousPageClick = () => {
    setDateFilters({
      sessionId: firstSession?.id,
      startDate: firstSession?.startedAt,
    });
  };
  const nextPageClick = () => {
    setDateFilters({
      endDate: lastSession?.startedAt,
      sessionId: lastSession?.id,
    });
  };
  const firstPageClick = () => {
    setDateFilters({});
  };

  return (
    <div className="flex flex-col min-h-[calc(100vh-120px)]">
      <UsersSessionsFilters appId={props.appId} onFiltersChange={setTopFilters} />
      {persistentUserId && (
        <div className="mb-2">
          <span className="inline-flex items-center gap-1 rounded-full border px-3 py-1 text-sm">
            Sessions for user
            <span className="font-mono" title={persistentUserId}>
              {formatPersistentUserId(persistentUserId)}
            </span>
            <button
              className="ml-1 text-muted-foreground hover:text-foreground"
              title="Show all sessions"
              onClick={clearUserFilter}
            >
              <IconX className="h-4 w-4" />
            </button>
          </span>
        </div>
      )}
      <div className="mb-auto">
        <SessionsGridDisplay appId={props.appId} sessions={sessions} isLoading={isPlaceholderData} />
      </div>

      <div className="flex justify-end items-center mt-4 mr-16 ">
        <Select value={pageSize.toString()} onValueChange={setPageSize}>
          <SelectTrigger className="w-36 mr-4">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SESSIONS_PAGE_SIZES.map((size) => (
              <SelectItem key={size} value={size.toString()}>
                {size} per page
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {!!dateFilters.sessionId && (
          <Button disabled={isPlaceholderData} variant="ghost" onClick={firstPageClick}>
            <IconChevronsLeft></IconChevronsLeft>
            First
          </Button>
        )}
        {(!!dateFilters.sessionId || isPlaceholderData) && (
          <Button disabled={isPlaceholderData} variant="ghost" onClick={previousPageClick}>
            <IconChevronLeft></IconChevronLeft>
            Previous
          </Button>
        )}
        {(sessions.length >= pageSize || isPlaceholderData) && (
          <Button disabled={isPlaceholderData} variant="ghost" onClick={nextPageClick}>
            Next
            <IconChevronRight></IconChevronRight>
          </Button>
        )}
      </div>
    </div>
  );
}
