import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@components/Dialog";
import { LoadingState } from "@components/LoadingState";
import { useApps } from "@features/apps";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { SymbolicatedStackTrace } from "./SymbolicatedStackTrace";

interface ErrorDetail {
  errorId: string;
  appId: string;
  timestamp: string;
  errorMessage: string;
  errorType: string;
  stackTrace: string;
  platform: string;
  osName: string;
  osVersion: string;
  appVersion: string;
  sdkVersion: string;
  sessionId: string;
  severity: string;
  kind: string;
}

interface ErrorDetailModalProps {
  appId: string;
  errorId: string | null;
  open: boolean;
  onClose: () => void;
}

async function fetchErrorDetail(appId: string, errorId: string, buildMode: string): Promise<ErrorDetail> {
  const params = new URLSearchParams({ buildMode });
  const response = await fetch(`/api/v0/apps/${appId}/errors/${errorId}?${params}`, {
    credentials: "include",
  });

  if (!response.ok) {
    throw new Error("Failed to fetch error details");
  }

  return response.json();
}

export function ErrorDetailModal({ appId, errorId, open, onClose }: ErrorDetailModalProps) {
  const { buildMode } = useApps();
  const navigate = useNavigate();

  const { data: error, isLoading } = useQuery({
    queryKey: ["error-detail", appId, buildMode, errorId],
    queryFn: () => fetchErrorDetail(appId, errorId!, buildMode),
    enabled: !!errorId && open,
  });

  const handleClickSessionId = (sessionId: string) => () => {
    // Get the current location to preserve search params
    const currentLocation = window.location;

    navigate(`/${appId}/live/${sessionId}`, {
      state: {
        returnTo: {
          pathname: currentLocation.pathname,
          search: currentLocation.search,
        },
      },
    });
  };

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-4xl max-h-[90vh] overflow-y-auto">
        {isLoading ? (
          <div className="py-8">
            <LoadingState size="md" />
          </div>
        ) : error ? (
          <>
            <DialogHeader>
              <DialogTitle>Error Details</DialogTitle>
              <DialogDescription>{error.errorType}</DialogDescription>
            </DialogHeader>

            <div className="space-y-6 mt-4">
              {/* Error Information Section */}
              <div>
                <h3 className="text-sm font-semibold mb-3">Error Information</h3>
                <div className="space-y-2 text-sm">
                  <div className="grid grid-cols-4 gap-2">
                    <span className="text-muted-foreground">Timestamp:</span>
                    <span className="col-span-3">{new Date(error.timestamp).toLocaleString()}</span>
                  </div>
                  <div className="grid grid-cols-4 gap-2">
                    <span className="text-muted-foreground">Error Type:</span>
                    <span className="col-span-3 font-medium">{error.errorType}</span>
                  </div>
                  {error.severity && (
                    <div className="grid grid-cols-4 gap-2">
                      <span className="text-muted-foreground">Severity:</span>
                      <span className="col-span-3 font-medium capitalize">{error.severity}</span>
                    </div>
                  )}
                  {error.kind && (
                    <div className="grid grid-cols-4 gap-2">
                      <span className="text-muted-foreground">Kind:</span>
                      <span className="col-span-3 capitalize">{error.kind}</span>
                    </div>
                  )}
                  <div className="grid grid-cols-4 gap-2">
                    <span className="text-muted-foreground">Message:</span>
                    <span className="col-span-3">{error.errorMessage}</span>
                  </div>
                </div>
              </div>

              {/* Device/Platform Section */}
              <div>
                <h3 className="text-sm font-semibold mb-3">Device & Platform Information</h3>
                <div className="space-y-2 text-sm">
                  <div className="grid grid-cols-4 gap-2">
                    <span className="text-muted-foreground">Platform:</span>
                    <span className="col-span-3">{error.platform}</span>
                  </div>
                  <div className="grid grid-cols-4 gap-2">
                    <span className="text-muted-foreground">OS:</span>
                    <span className="col-span-3">
                      {error.osName} {error.osVersion}
                    </span>
                  </div>
                  <div className="grid grid-cols-4 gap-2">
                    <span className="text-muted-foreground">App Version:</span>
                    <span className="col-span-3">{error.appVersion}</span>
                  </div>
                  <div className="grid grid-cols-4 gap-2">
                    <span className="text-muted-foreground">SDK Version:</span>
                    <span className="col-span-3">{error.sdkVersion}</span>
                  </div>
                  {error.sessionId && (
                    <div className="grid grid-cols-4 gap-2">
                      <span className="text-muted-foreground">Session ID:</span>
                      <button
                        className="col-span-3 font-mono text-xs text-left"
                        onClick={handleClickSessionId(error.sessionId)}
                      >
                        {error.sessionId}
                      </button>
                    </div>
                  )}
                </div>
              </div>

              {/* Stack Trace Section */}
              {error.stackTrace && (
                <SymbolicatedStackTrace
                  appId={appId}
                  errorId={error.errorId}
                  buildMode={buildMode}
                  stackTrace={error.stackTrace}
                />
              )}
            </div>
          </>
        ) : (
          <div className="py-8 text-center text-muted-foreground">Error not found</div>
        )}
      </DialogContent>
    </Dialog>
  );
}
