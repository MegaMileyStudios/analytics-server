import { Alert, AlertDescription, AlertTitle } from "@components/Alert";
import { Badge } from "@components/Badge";
import { Button } from "@components/Button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@components/Tabs";
import { IconAlertTriangle, IconCheck, IconCopy, IconRefresh } from "@tabler/icons-react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { formatDistanceToNow } from "date-fns";
import { ReactNode, useState } from "react";
import { twMerge } from "tailwind-merge";

type ResultSymbol = {
  function: string;
  rawFunction: string;
  location?: string;
  noSourceInfo: boolean;
  inlined: boolean;
};

type ResultFrame = {
  kind: "native" | "managed";
  index?: number;
  module: string;
  moduleRole: "engine" | "game" | "system";
  offset?: string;
  status: "resolved" | "named" | "unresolved";
  crashHandler: boolean;
  reason?: string;
  reportedName?: string;
  symbols: ResultSymbol[];
};

type ResultLine = {
  text: string;
  frame?: ResultFrame;
};

type SymbolicationResult = {
  buildId?: string;
  rid?: string;
  resolvedFrames: number;
  totalFrames: number;
  missing: { module: string; key: string }[];
  lines: ResultLine[];
};

type SymbolicationResponse = {
  eligible: boolean;
  available: boolean;
  symbolication: { symbolicatedAt: string; result: SymbolicationResult; text: string } | null;
};

type Props = {
  appId: string;
  errorId: string;
  buildMode: string;
  stackTrace: string;
};

const roleVariants = { engine: "secondary", game: "default", system: "outline" } as const;

async function fetchSymbolication(appId: string, errorId: string, buildMode: string, method: "GET" | "POST") {
  const params = new URLSearchParams({ buildMode });
  const path = method === "GET" ? "symbolication" : "symbolicate";
  const response = await fetch(`/api/v0/apps/${appId}/errors/${errorId}/${path}?${params}`, {
    method,
    credentials: "include",
  });

  if (!response.ok) {
    throw new Error("Failed to fetch the symbolicated stack trace");
  }

  return (await response.json()) as SymbolicationResponse;
}

function CopyButton(props: { text: string }) {
  const [justCopied, setJustCopied] = useState(false);

  const copy = () => {
    navigator.clipboard.writeText(props.text);
    setJustCopied(true);
    setTimeout(() => setJustCopied(false), 2000);
  };

  return (
    <button
      onClick={copy}
      className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground transition-colors"
    >
      {justCopied ? <IconCheck className="h-4 w-4" /> : <IconCopy className="h-4 w-4" />}
      <span>{justCopied ? "Copied!" : "Copy"}</span>
    </button>
  );
}

function FrameRow(props: { frame: ResultFrame }) {
  const { frame } = props;
  const label = frame.index !== undefined ? `[${frame.index}]` : "at";

  let body: ReactNode;
  if (frame.symbols.length > 0) {
    body = frame.symbols.map((symbol, i) => (
      <div key={i} className={twMerge(i > 0 && "pl-4")}>
        <span className="font-semibold text-foreground">{symbol.function}</span>
        {symbol.location && <span className="text-muted-foreground"> {symbol.location}</span>}
        {!symbol.location && symbol.noSourceInfo && <span className="text-muted-foreground italic"> (no source info)</span>}
        {symbol.inlined && <span className="text-muted-foreground italic"> inlined into</span>}
      </div>
    ));
  } else if (frame.status === "named") {
    body = (
      <div>
        <span className="text-foreground">{frame.reportedName}</span>
        <span className="text-muted-foreground italic"> (named in the report)</span>
      </div>
    );
  } else {
    body = (
      <div>
        <span className="text-foreground">
          {frame.module}+{frame.offset}
        </span>
        <span className="text-muted-foreground italic"> {frame.reason}</span>
      </div>
    );
  }

  return (
    <div className={twMerge("flex gap-3 py-0.5", frame.crashHandler && "opacity-50")} title={frame.crashHandler ? "Crash handler" : undefined}>
      <span className="text-muted-foreground w-10 shrink-0 text-right">{label}</span>
      <div className="min-w-0 flex-1 break-words">{body}</div>
      <Badge variant={roleVariants[frame.moduleRole]} className="shrink-0 self-start font-mono font-normal">
        {frame.module}
      </Badge>
    </div>
  );
}

// Text lines are shown as they are, the Modules list folds away and frames get their symbols
function SymbolicatedLines(props: { lines: ResultLine[] }) {
  const blocks: ReactNode[] = [];
  let modules: string[] | null = null;

  props.lines.forEach((line, i) => {
    if (modules !== null && !line.frame && line.text.trim() !== "" && !line.text.startsWith("--")) {
      modules.push(line.text);
      return;
    }
    if (modules !== null) {
      const list = modules;
      blocks.push(
        <details key={`modules-${i}`} className="my-1">
          <summary className="cursor-pointer text-muted-foreground">Modules ({list.length})</summary>
          <div className="pl-4 text-muted-foreground">
            {list.map((m, j) => (
              <div key={j}>{m}</div>
            ))}
          </div>
        </details>
      );
      modules = null;
    }

    if (line.frame) {
      blocks.push(<FrameRow key={i} frame={line.frame} />);
    } else if (line.text.trim() === "Modules:") {
      modules = [];
    } else {
      blocks.push(
        <div key={i} className="text-muted-foreground whitespace-pre-wrap">
          {line.text || " "}
        </div>
      );
    }
  });

  return <div className="bg-muted p-4 rounded-md text-xs font-mono overflow-x-auto">{blocks}</div>;
}

export function SymbolicatedStackTrace(props: Props) {
  const queryClient = useQueryClient();
  const [symbolicating, setSymbolicating] = useState(false);
  const queryKey = ["error-symbolication", props.appId, props.buildMode, props.errorId];

  const { data } = useQuery({
    queryKey,
    queryFn: () => fetchSymbolication(props.appId, props.errorId, props.buildMode, "GET"),
    // New reports are symbolicated in the background within seconds
    refetchInterval: (query) => {
      const response = query.state.data;
      return response?.eligible && response.available && !response.symbolication ? 10_000 : false;
    },
  });

  const symbolicate = async () => {
    setSymbolicating(true);
    try {
      const response = await fetchSymbolication(props.appId, props.errorId, props.buildMode, "POST");
      queryClient.setQueryData(queryKey, response);
    } finally {
      setSymbolicating(false);
    }
  };

  const symbolication = data?.symbolication;
  const result = symbolication?.result;
  const original = (
    <pre className="bg-muted p-4 rounded-md text-xs font-mono overflow-x-auto whitespace-pre-wrap break-words">
      {props.stackTrace}
    </pre>
  );

  if (!data?.eligible) {
    return (
      <div>
        <div className="flex items-center justify-between mb-3">
          <h3 className="text-sm font-semibold">Stack Trace</h3>
          <CopyButton text={props.stackTrace} />
        </div>
        {original}
      </div>
    );
  }

  if (!result || result.totalFrames === 0) {
    return (
      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold">Stack Trace</h3>
          <CopyButton text={props.stackTrace} />
        </div>
        {!data.available ? (
          <p className="text-xs text-muted-foreground">Symbolication is unavailable on this server.</p>
        ) : (
          !result && (
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              <span>Not symbolicated yet, this happens automatically within a minute.</span>
              <Button size="xs" variant="outline" onClick={symbolicate} loading={symbolicating}>
                Symbolicate now
              </Button>
            </div>
          )
        )}
        {original}
      </div>
    );
  }

  return (
    <Tabs defaultValue="symbolicated">
      <div className="flex items-center justify-between gap-4">
        <h3 className="text-sm font-semibold">Stack Trace</h3>
        <TabsList className="w-auto border-b-0">
          <TabsTrigger value="symbolicated">Symbolicated</TabsTrigger>
          <TabsTrigger value="original">Original</TabsTrigger>
        </TabsList>
      </div>

      <TabsContent value="symbolicated" className="space-y-3">
        <div className="flex items-center justify-between gap-4 text-xs text-muted-foreground">
          <span>
            {result.resolvedFrames} of {result.totalFrames} frames resolved · symbolicated{" "}
            {formatDistanceToNow(new Date(symbolication.symbolicatedAt), { addSuffix: true })}
          </span>
          <div className="flex items-center gap-4">
            <Button size="xs" variant="ghost" onClick={symbolicate} disabled={!data.available || symbolicating}>
              <IconRefresh className={twMerge("h-4 w-4 mr-1", symbolicating && "animate-spin")} />
              Re-symbolicate
            </Button>
            <CopyButton text={symbolication.text} />
          </div>
        </div>

        {result.missing.length > 0 && (
          <Alert variant="warning">
            <IconAlertTriangle className="h-4 w-4" />
            <AlertTitle>Missing symbols</AlertTitle>
            <AlertDescription>
              <ul className="text-xs space-y-0.5">
                {result.missing.map((m) => (
                  <li key={m.key}>
                    {m.module}: <span className="font-mono">{m.key}</span>
                  </li>
                ))}
              </ul>
              <p className="text-xs mt-1">The report is symbolicated again as soon as they're uploaded.</p>
            </AlertDescription>
          </Alert>
        )}

        <SymbolicatedLines lines={result.lines} />
      </TabsContent>

      <TabsContent value="original" className="space-y-3">
        <div className="flex justify-end">
          <CopyButton text={props.stackTrace} />
        </div>
        {original}
      </TabsContent>
    </Tabs>
  );
}
