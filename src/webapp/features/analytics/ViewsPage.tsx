import { trackEvent } from "@aptabase/web";
import { Page, PageHeading } from "@components/Page";
import { useApps, useCurrentApp } from "@features/apps";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useAtomValue } from "jotai";
import { useEffect } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { dateFilterValuesAtom } from "../../atoms/date-atoms";
import { CurrentFilters } from "./CurrentFilters";
import { DateFilterContainer } from "./date-filters/DateFilterContainer";
import { BuildModeSelector } from "./mode/BuildModeSelector";
import { DebugModeBanner } from "./mode/DebugModeBanner";
import { QueryParams, viewMainStats, viewPopupStats } from "./query";
import { ViewFilterWidgets } from "./views/ViewFilterWidgets";
import { ViewStatsTable } from "./views/ViewStatsTable";

Component.displayName = "ViewsPage";
export function Component() {
  const app = useCurrentApp();
  const { buildMode } = useApps();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { startDateIso, endDateIso } = useAtomValue(dateFilterValuesAtom);

  const countryCode = searchParams.get("countryCode") || "";
  const osName = searchParams.get("osName") || "";
  const appVersion = searchParams.get("appVersion") || "";
  const mainView = searchParams.get("mainView") || "";

  const params: QueryParams = {
    appId: app?.id ?? "",
    buildMode,
    startDate: startDateIso,
    endDate: endDateIso,
    countryCode,
    osName,
    appVersion,
  };
  const filterKey = [app?.id, buildMode, startDateIso, endDateIso, countryCode, osName, appVersion];
  const enabled = !!app && !!startDateIso && !!endDateIso;

  const main = useQuery({
    queryKey: ["view-main-stats", ...filterKey],
    queryFn: () => viewMainStats(params),
    placeholderData: keepPreviousData,
    enabled,
  });

  const popups = useQuery({
    queryKey: ["view-popup-stats", ...filterKey, mainView],
    queryFn: () => viewPopupStats({ ...params, mainView }),
    placeholderData: keepPreviousData,
    enabled,
  });

  useEffect(() => {
    trackEvent("views_viewed");
  }, []);

  if (!app) return <Navigate to="/" />;

  const toggleMainView = (name: string) => {
    setSearchParams(
      (current) => {
        if (current.get("mainView") === name) current.delete("mainView");
        else current.set("mainView", name);
        return current;
      },
      { preventScrollReset: true }
    );
  };

  return (
    <Page title="Views">
      {buildMode === "debug" && <DebugModeBanner />}
      <div className="space-y-4">
        <div className="flex justify-between items-center">
          <PageHeading
            title="Views"
            subtitle="Time spent on each screen and popup"
            onClick={() => navigate(`/${app.id}/views`)}
          />
          <div className="flex items-end space-x-2">
            <BuildModeSelector />
            <DateFilterContainer />
          </div>
        </div>
        <div className="flex w-full justify-end">
          <CurrentFilters />
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <ViewStatsTable
            title="Main Views"
            nameLabel="View"
            rows={main.data ?? []}
            isLoading={main.isLoading}
            isError={main.isError}
            refetch={main.refetch}
            selected={mainView}
            onSelect={toggleMainView}
          />
          <ViewStatsTable
            title={
              mainView ? (
                <>
                  Popups in <span className="font-bold">{mainView}</span>
                </>
              ) : (
                "Popups in all views"
              )
            }
            nameLabel="Popup"
            rows={popups.data ?? []}
            isLoading={popups.isLoading}
            isError={popups.isError}
            refetch={popups.refetch}
          />
        </div>

        <ViewFilterWidgets appId={app.id} />

        <p className="text-xs text-muted-foreground">
          A main view lasts until the next main view is shown or the session ends, including the time popups were
          open on top of it. Popups that weren't closed explicitly end when the main view changes or the session ends.
        </p>
      </div>
    </Page>
  );
}
