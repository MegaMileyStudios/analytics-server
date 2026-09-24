import { trackEvent } from "@aptabase/web";
import { EmptyState } from "@components/EmptyState";
import { ErrorState } from "@components/ErrorState";
import { LoadingState } from "@components/LoadingState";
import { Page, PageHeading } from "@components/Page";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@components/Select";
import { ToggleGroup, ToggleGroupList, ToggleGroupTrigger } from "@components/ToggleGroup";
import { useApps, useCurrentApp } from "@features/apps";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { Navigate, useSearchParams } from "react-router-dom";
import { BuildModeSelector } from "./mode/BuildModeSelector";
import { DebugModeBanner } from "./mode/DebugModeBanner";
import { CohortGranularity, retentionCohorts } from "./query";
import { RetentionTable } from "./retention/RetentionTable";

type RangeOption = { cohorts: number; name: string };

const ranges: Record<CohortGranularity, { options: RangeOption[]; defaultCohorts: number }> = {
  day: {
    options: [
      { cohorts: 7, name: "Last 7 days" },
      { cohorts: 14, name: "Last 14 days" },
      { cohorts: 30, name: "Last 30 days" },
      { cohorts: 60, name: "Last 60 days" },
    ],
    defaultCohorts: 14,
  },
  week: {
    options: [
      { cohorts: 4, name: "Last 4 weeks" },
      { cohorts: 8, name: "Last 8 weeks" },
      { cohorts: 12, name: "Last 12 weeks" },
      { cohorts: 26, name: "Last 26 weeks" },
    ],
    defaultCohorts: 8,
  },
  month: {
    options: [
      { cohorts: 3, name: "Last 3 months" },
      { cohorts: 6, name: "Last 6 months" },
      { cohorts: 12, name: "Last 12 months" },
    ],
    defaultCohorts: 6,
  },
};

function isGranularity(value: string | null): value is CohortGranularity {
  return value === "day" || value === "week" || value === "month";
}

// Cohorts are bucketed in UTC by the API, so the range is sent as plain UTC dates
function cohortRange(granularity: CohortGranularity, cohorts: number): { startDate: string; endDate: string } {
  const today = new Date();
  const start = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate()));
  if (granularity === "day") start.setUTCDate(start.getUTCDate() - (cohorts - 1));
  if (granularity === "week") start.setUTCDate(start.getUTCDate() - 7 * (cohorts - 1));
  if (granularity === "month") start.setUTCMonth(start.getUTCMonth() - (cohorts - 1), 1);

  return { startDate: start.toISOString().slice(0, 10), endDate: today.toISOString().slice(0, 10) };
}

Component.displayName = "RetentionPage";
export function Component() {
  const app = useCurrentApp();
  const { buildMode } = useApps();
  const [searchParams, setSearchParams] = useSearchParams();

  const granularityParam = searchParams.get("granularity");
  const granularity: CohortGranularity = isGranularity(granularityParam) ? granularityParam : "day";
  const { options, defaultCohorts } = ranges[granularity];
  const cohortsParam = Number(searchParams.get("cohorts"));
  const cohorts = options.some((o) => o.cohorts === cohortsParam) ? cohortsParam : defaultCohorts;
  // One period less than cohorts, so the oldest cohort's row is complete and the table is a triangle
  const periods = Math.max(1, cohorts - 1);

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ["retention", app?.id, buildMode, granularity, cohorts],
    queryFn: () =>
      retentionCohorts({ appId: app!.id, buildMode, granularity, periods, ...cohortRange(granularity, cohorts) }),
    placeholderData: keepPreviousData,
    enabled: !!app,
  });

  useEffect(() => {
    trackEvent("retention_viewed");
  }, []);

  if (!app) return <Navigate to="/" />;

  const setParam = (name: string, value: string | undefined) => {
    setSearchParams((params) => {
      if (value === undefined) params.delete(name);
      else params.set(name, value);
      return params;
    });
  };

  const changeGranularity = (value: string) => {
    setSearchParams((params) => {
      params.set("granularity", value);
      params.delete("cohorts");
      return params;
    });
  };

  return (
    <Page title="Retention">
      {buildMode === "debug" && <DebugModeBanner />}
      <div className="flex justify-between items-center">
        <PageHeading
          title="Retention"
          subtitle={`Share of new players that came back, grouped by the ${granularity} they were first seen`}
        />
        <div className="flex items-center">
          <BuildModeSelector />
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2 mt-4">
        <ToggleGroup value={granularity} onValueChange={changeGranularity}>
          <ToggleGroupList>
            <ToggleGroupTrigger value="day">Daily</ToggleGroupTrigger>
            <ToggleGroupTrigger value="week">Weekly</ToggleGroupTrigger>
            <ToggleGroupTrigger value="month">Monthly</ToggleGroupTrigger>
          </ToggleGroupList>
        </ToggleGroup>
        <Select value={cohorts.toString()} onValueChange={(value) => setParam("cohorts", value)}>
          <SelectTrigger className="w-44">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {options.map((o) => (
              <SelectItem key={o.cohorts} value={o.cohorts.toString()}>
                {o.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="mt-4">
        {isLoading ? (
          <div className="h-64">
            <LoadingState />
          </div>
        ) : isError || !data ? (
          <div className="h-64">
            <ErrorState refetch={refetch} />
          </div>
        ) : data.cohorts.length === 0 ? (
          <div className="h-64">
            <EmptyState />
          </div>
        ) : (
          <RetentionTable data={data} granularity={granularity} />
        )}
      </div>

      <p className="mt-4 text-xs text-muted-foreground">
        Only players that send a persistent user ID are counted, anonymous players can't be followed over time. Days,
        weeks (starting on Monday) and months are in UTC. The latest {granularity} of each cohort may
        still be in progress.
      </p>
    </Page>
  );
}
