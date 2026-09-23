import { Button } from "@components/Button";
import { SessionStartEndDateFilters, SessionsGridDisplay } from "@features/analytics/sessions/SessionsGridDisplay";
import { formatPersistentUserId } from "@fns/format-persistent-user-id";
import { IconChevronLeft, IconChevronRight, IconChevronsLeft, IconX } from "@tabler/icons-react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useSearchParams } from "react-router-dom";
import { QueryParams, historicalSessions } from "../query";
import { UsersSessionsFilters } from "./UsersSessionsFilters";

const SESSIONS_PAGE_SIZE = 10;

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
  const location = useLocation();
  const locationStateFilters = location.state?.sessionFilters;
  const [dateFilters, setDateFilters] = useState<SessionStartEndDateFilters>(locationStateFilters || {});
  const [topFilters, setTopFilters] = useState<TopFilterValue>({});
  const [searchParams, setSearchParams] = useSearchParams();
  const persistentUserId = searchParams.get("userId") || undefined;

  // Start from the first page when switching to (or away from) a single user's sessions
  const previousUserId = useRef(persistentUserId);
  useEffect(() => {
    if (previousUserId.current === persistentUserId) return;
    previousUserId.current = persistentUserId;
    setDateFilters({});
  }, [persistentUserId]);

  const clearUserFilter = () => {
    setSearchParams((params) => {
      params.delete("userId");
      return params;
    });
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
    };

    const params = Object.fromEntries(Object.entries(allParams).filter(([_, v]) => v != null));
    return params as QueryParams;
  }, [props, dateFilters, topFilters, persistentUserId]);

  const { data, isSuccess, isPlaceholderData } = useQuery({
    queryKey: ["historical-sessions", props.appId, props.buildMode, dateFilters, topFilters, persistentUserId],
    queryFn: () => historicalSessions(requestParams),
    placeholderData: keepPreviousData,
  });

  const sessions = [...(data || [])];
  if (dateFilters.startDate) {
    sessions.reverse();
  }

  // When we reach first page and hit previous one more time
  useEffect(() => {
    if (isSuccess && sessions.length === 0 && dateFilters.startDate) {
      setDateFilters({});
    }
  }, [sessions]);

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
        <SessionsGridDisplay
          appId={props.appId}
          sessions={sessions}
          sessionFilters={dateFilters}
          isLoading={isPlaceholderData}
        />
      </div>

      <div className="flex justify-end mt-4 mr-16 ">
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
        {(sessions.length >= SESSIONS_PAGE_SIZE || isPlaceholderData) && (
          <Button disabled={isPlaceholderData} variant="ghost" onClick={nextPageClick}>
            Next
            <IconChevronRight></IconChevronRight>
          </Button>
        )}
      </div>
    </div>
  );
}
