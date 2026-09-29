import { trackEvent } from "@aptabase/web";
import { Button } from "@components/Button";
import { Page, PageHeading } from "@components/Page";
import { liveSessionDetails } from "@features/analytics/query";
import { useApps, useCurrentApp } from "@features/apps";
import { CountryFlag, CountryName } from "@features/geo";
import { formatDate, formatTime } from "@fns/format-date";
import { formatNumber } from "@fns/format-number";
import { formatPersistentUserId } from "@fns/format-persistent-user-id";
import { IconArrowLeft, IconClick, IconClock, IconDevices, IconFingerprint, IconUser } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { Navigate, useLocation, useNavigate, useParams } from "react-router-dom";
import { OSIcon } from "./dashboard/icons/os";
import { SessionTimeline } from "./liveview/timeline";

Component.displayName = "LiveSessionDetailsPage";
export function Component() {
  const { buildMode } = useApps();
  const app = useCurrentApp();
  const { sessionId } = useParams();
  const location = useLocation();
  const navigate = useNavigate();

  if (!app || !sessionId) return <Navigate to="/" />;

  const { isLoading, data } = useQuery({
    queryKey: ["live-session-details", app.id, buildMode, sessionId],
    queryFn: () => liveSessionDetails({ appId: app.id, buildMode, sessionId }),
    refetchInterval: 10000,
  });

  useEffect(() => {
    trackEvent("liveview_session_viewed");
  }, []);

  const handleBack = () => {
    // We came from a list in this app, so go back to that exact history entry (and page)
    if (location.state?.returnTo) {
      navigate(-1);
    } else {
      navigate(`/${app.id}/live/`);
    }
  };

  return (
    <Page title="Live View">
      <div className="flex flex-row justify-between items-center">
        <PageHeading title="Session Timeline" subtitle={sessionId} />
        <Button className="mb-5" variant="ghost" onClick={handleBack}>
          <IconArrowLeft /> Back
        </Button>
      </div>

      {data && (
        <div className="mt-10 flex flex-col">
          <div className="flex gap-2 items-center mb-1">
            <IconDevices className="text-muted-foreground h-5 w-5" />
            <span className="tabular-nums">App Version {data.appVersion}</span>
          </div>
          <div className="flex gap-2 items-center mb-1">
            <IconUser className="text-muted-foreground h-5 w-5" />
            <span className="tabular-nums">{`${formatDate(data.startedAt)} ${formatTime(data.startedAt)}`}</span>
          </div>
          {data.persistentUserId && (
            <div className="flex gap-2 items-center mb-1">
              <IconFingerprint className="text-muted-foreground h-5 w-5" />
              <span className="font-mono" title={data.persistentUserId}>
                {formatPersistentUserId(data.persistentUserId)}
              </span>
              <Button
                size="xs"
                variant="ghost"
                onClick={() =>
                  navigate(`/${app.id}/sessions?userId=${encodeURIComponent(data.persistentUserId ?? "")}`)
                }
              >
                View all sessions from this user
              </Button>
            </div>
          )}

          <div className="flex flex-col space-y-1 md:flex-row md:space-y-0">
            <div className="w-40 space-y-1">
              <div className="flex gap-2 items-center">
                <IconClock className="text-muted-foreground h-5 w-5" />
                <span className="tabular-nums">{formatNumber(data.duration, "duration")}</span>
              </div>

              <div className="flex gap-2 items-center">
                <IconClick className="text-muted-foreground h-5 w-5" />
                <span>{data.eventsCount} events</span>
              </div>
            </div>

            <div className="space-y-1">
              <div className="flex gap-2 items-center">
                <CountryFlag countryCode={data.countryCode} />
                <div>
                  {data.regionName && <span>{data.regionName} · </span>} <CountryName countryCode={data.countryCode} />
                </div>
              </div>

              <div className="flex gap-2 items-center">
                <OSIcon name={data.osName} className="h-5 w-5" />
                <span>
                  {data.osName} {data.osVersion}
                </span>
              </div>
            </div>
          </div>

          <div className="mt-8 flex flex-col gap-4">
            <SessionTimeline {...data} />
          </div>
        </div>
      )}
    </Page>
  );
}
