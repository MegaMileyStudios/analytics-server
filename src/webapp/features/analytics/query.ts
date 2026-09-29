import { api } from "@fns/api";
import { QueryObserverResult, RefetchOptions } from "@tanstack/react-query";

export type TopNItem = {
  name: string;
  value: number;
};

export type QueryParams = {
  buildMode: "release" | "debug";
  appId: string;
  sessionId?: string;
  startDate?: string;
  endDate?: string;
  granularity?: Granularity;
  countryCode?: string;
  osName?: string;
  eventName?: string;
  appVersion?: string;
  persistentUserId?: string;
  propKey?: string;
  pageSize?: number;
  mainView?: string;
};

export type TopNQueryChildrenProps = {
  isLoading: boolean;
  isError: boolean;
  items: TopNItem[];
  refetch?: (options?: RefetchOptions | undefined) => Promise<QueryObserverResult<TopNItem[], Error>>;
};

export function topCountries(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/top-countries`, params);
}

export function topRegions(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/top-regions`, params);
}

export function topOperatingSystem(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/top-operatingsystems`, params);
}

export function topOSVersions(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/top-osversions`, params);
}

export function topEvents(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/top-events`, params);
}

export function topAppVersions(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/top-appversions`, params);
}

export function topAppBuildNumbers(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/top-appbuildnumbers`, params);
}

type KeyMetricsRow = {
  dailyUsers: number;
  dailyPersistentUsers: number;
  dailyInferredUsers: number;
  sessions: number;
  events: number;
  durationSeconds: number;
};

export type KeyMetrics = {
  current: KeyMetricsRow;
  previous?: KeyMetricsRow;
};

export function keyMetrics(params: QueryParams): Promise<KeyMetrics> {
  return api.get<KeyMetrics>(`/_stats/metrics`, params);
}

export type Granularity = "hour" | "day" | "month";

export type PeriodicStats = Array<{
  period: string;
  users: number;
  persistentUsers: number;
  inferredUsers: number;
  sessions: number;
  persistentSessions: number;
  inferredSessions: number;
  events: number;
  persistentEvents: number;
  inferredEvents: number;
}>;

export function periodicStats(params: QueryParams): Promise<PeriodicStats> {
  return api.get<PeriodicStats>(`/_stats/periodic`, params);
}

export type EventPropType = "number" | "boolean" | "string";

export type EventPropKey = {
  key: string;
  type: EventPropType;
};

export function eventPropKeys(params: QueryParams): Promise<EventPropKey[]> {
  return api.get<EventPropKey[]>(`/_stats/event-prop-keys`, params);
}

export type EventPropNumericRow = {
  period: string;
  events: number;
  min: number;
  max: number;
  avg: number;
  median: number;
  p95: number;
  p99: number;
  sum: number;
};

export function eventPropNumeric(params: QueryParams): Promise<EventPropNumericRow[]> {
  return api.get<EventPropNumericRow[]>(`/_stats/event-prop-numeric`, params);
}

export type EventPropDistributionItem = {
  name: string;
  value: number;
  isOther: boolean;
  isUndefined: boolean;
};

export type EventPropDistribution = {
  type: "boolean" | "string";
  items: EventPropDistributionItem[];
};

export function eventPropDistribution(params: QueryParams): Promise<EventPropDistribution> {
  return api.get<EventPropDistribution>(`/_stats/event-prop-distribution`, params);
}

export type EventPropDistributionSeries = {
  type: "boolean" | "string";
  periods: string[];
  series: Array<{
    name: string;
    isOther: boolean;
    isUndefined: boolean;
    values: number[];
  }>;
};

export function eventPropDistributionPeriodic(params: QueryParams): Promise<EventPropDistributionSeries> {
  return api.get<EventPropDistributionSeries>(`/_stats/event-prop-distribution-periodic`, params);
}

export type CohortGranularity = "day" | "week" | "month";

export type RetentionParams = {
  buildMode: "release" | "debug";
  appId: string;
  startDate: string;
  endDate: string;
  granularity: CohortGranularity;
  periods: number;
};

export type RetentionCohort = {
  cohortDate: string;
  size: number;
  retained: Array<number | null>;
};

export type RetentionCohorts = {
  granularity: string;
  periods: number;
  cohorts: RetentionCohort[];
};

export function retentionCohorts(params: RetentionParams): Promise<RetentionCohorts> {
  return api.get<RetentionCohorts>(`/_stats/retention`, params);
}

type EventPropsItem = {
  stringKey: string;
  stringValue: string;
  numericKey: string;
  events: number;
  median: number;
  min: number;
  max: number;
  sum: number;
};

export function topEventProps(params: QueryParams): Promise<EventPropsItem[]> {
  return api.get<EventPropsItem[]>(`/_stats/top-props`, params);
}

type LiveGeoDataPoint = {
  countryCode: string;
  regionName: string;
  latitude: number;
  longitude: number;
  users: number;
  persistentUsers: number;
  inferredUsers: number;
};

export function liveGeoDataPoints(params: QueryParams): Promise<LiveGeoDataPoint[]> {
  return api.get<LiveGeoDataPoint[]>(`/_stats/live-geo`, params);
}

export type LiveRecentSession = {
  id: string;
  eventsCount: number;
  startedAt: string;
  duration: number;
  appVersion: string;
  countryCode: string;
  regionName: string;
  osName: string;
  osVersion: string;
  persistentUserId?: string | null;
};

export function liveRecentSessions(params: QueryParams): Promise<LiveRecentSession[]> {
  return api.get<LiveRecentSession[]>(`/_stats/live-sessions`, params);
}

export function historicalSessions(params: QueryParams): Promise<LiveRecentSession[]> {
  return api.get<LiveRecentSession[]>(`/_stats/historical-sessions`, params);
}

export type SessionTimeline = {
  eventsName: string[];
  eventsTimestamp: string[];
  eventsStringProps: string[];
  eventsNumericProps: string[];
  viewsType?: string[];
  viewsName?: string[];
  viewsTimestamp?: string[];
};

export function liveSessionDetails(params: QueryParams): Promise<LiveRecentSession & SessionTimeline> {
  return api.get<LiveRecentSession & SessionTimeline>(`/_stats/live-session-details`, params);
}

export type ViewStatsRow = {
  name: string;
  visits: number;
  sessions: number;
  totalSeconds: number;
  avgSeconds: number;
  medianSeconds: number;
};

export function viewMainStats(params: QueryParams): Promise<ViewStatsRow[]> {
  return api.get<ViewStatsRow[]>(`/_stats/views/main`, params);
}

export function viewPopupStats(params: QueryParams): Promise<ViewStatsRow[]> {
  return api.get<ViewStatsRow[]>(`/_stats/views/popups`, params);
}

export function viewTopCountries(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/views/top-countries`, params);
}

export function viewTopOperatingSystems(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/views/top-operatingsystems`, params);
}

export function viewTopAppVersions(params: QueryParams): Promise<TopNItem[]> {
  return api.get<TopNItem[]>(`/_stats/views/top-appversions`, params);
}
