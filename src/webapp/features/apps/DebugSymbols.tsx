import { Alert, AlertDescription, AlertTitle } from "@components/Alert";
import { Badge } from "@components/Badge";
import { Button } from "@components/Button";
import { ErrorState } from "@components/ErrorState";
import { LoadingState } from "@components/LoadingState";
import { TextInput } from "@components/TextInput";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@components/Tooltip";
import { api } from "@fns/api";
import { formatDate } from "@fns/format-date";
import { formatBytes } from "@fns/format-number";
import {
  IconAlertTriangle,
  IconCheck,
  IconChevronDown,
  IconChevronRight,
  IconCopy,
  IconFileCode,
  IconHelp,
  IconKey,
  IconTrash,
} from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Fragment, useState } from "react";

type SymbolUploadKey = {
  id: string;
  name: string;
  keyPrefix: string;
  createdByName: string;
  createdAt: string;
  lastUsedAt: string | null;
};

type SymbolFile = {
  debugId: string;
  format: string;
  arch: string | null;
  rid: string | null;
  moduleName: string;
  path: string;
  sizeBytes: number;
};

type SymbolUpload = {
  id: string;
  name: string;
  buildId: string | null;
  keyName: string | null;
  sizeBytes: number;
  createdAt: string;
  files: SymbolFile[];
};

type UploadsResponse = {
  available: boolean;
  unavailableMessage: string | null;
  uploads: SymbolUpload[];
};

const formatNames: Record<string, string> = { elf: "ELF", pdb: "PDB", macho: "dSYM" };

// Engine archives have no RID folders, their PDBs then only tell the platform
const platformNames: Record<string, string> = { elf: "linux", pdb: "windows", macho: "macos" };

function CopyButton(props: { text: string }) {
  const [copied, setCopied] = useState(false);

  const copy = () => {
    navigator.clipboard.writeText(props.text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <Button type="button" variant="ghost" size="xs" onClick={copy}>
      {copied ? <IconCheck className="w-4 h-4" /> : <IconCopy className="w-4 h-4" />}
    </Button>
  );
}

function UploadKeys() {
  const [name, setName] = useState("");
  const [created, setCreated] = useState<{ name: string; secret: string } | null>(null);

  const { isLoading, isError, data: keys, refetch } = useQuery({
    queryKey: ["symbol-upload-keys"],
    queryFn: () => api.get<SymbolUploadKey[]>("/_symbols/keys"),
  });

  const createKey = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    const result = await api.post<{ key: SymbolUploadKey; secret: string }>("/_symbols/keys", { name: name.trim() });
    setCreated({ name: result.key.name, secret: result.secret });
    setName("");
    refetch();
  };

  const revokeKey = async (key: SymbolUploadKey) => {
    if (!window.confirm(`Revoke the upload key '${key.name}'? CI jobs that use it can't upload symbols anymore.`)) return;

    await api.delete(`/_symbols/keys/${key.id}`);
    refetch();
  };

  if (isLoading) return <LoadingState />;
  if (isError) return <ErrorState />;

  return (
    <div>
      <div className="flex items-center space-x-2 mb-4">
        <IconKey className="h-5 w-5 text-foreground" />
        <h3 className="text-lg font-medium">Upload keys</h3>
      </div>

      {created && (
        <Alert className="max-w-[48rem] mb-4">
          <IconCheck className="h-4 w-4" />
          <AlertTitle>Key '{created.name}' created</AlertTitle>
          <AlertDescription className="text-muted-foreground">
            <p>Copy it now, it isn't shown again.</p>
            <div className="flex items-center gap-2 mt-2">
              <code className="bg-muted px-2 py-1 rounded text-xs font-mono break-all">{created.secret}</code>
              <CopyButton text={created.secret} />
            </div>
          </AlertDescription>
        </Alert>
      )}

      {keys && keys.length > 0 && (
        <ul className="space-y-2 mb-6 max-w-[48rem]">
          {keys.map((key) => (
            <li key={key.id} className="flex items-center bg-card space-x-4 border px-4 py-2 rounded-md justify-between">
              <div className="truncate">
                <p className="truncate">{key.name}</p>
                <p className="text-xs text-muted-foreground mt-1 truncate">
                  <span className="font-mono">{key.keyPrefix}…</span> · by {key.createdByName} on {formatDate(key.createdAt)}
                  {" · "}
                  {key.lastUsedAt ? `last used ${formatDate(key.lastUsedAt)}` : "never used"}
                </p>
              </div>
              <TooltipProvider>
                <Tooltip delayDuration={0}>
                  <TooltipTrigger asChild>
                    <Button variant="ghost" size="xs" onClick={() => revokeKey(key)}>
                      <IconTrash className="w-4 h-4" />
                    </Button>
                  </TooltipTrigger>
                  <TooltipContent>Revoke</TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={createKey} className="max-w-[40rem]">
        <div className="flex items-center space-x-2">
          <TextInput
            label="New key:"
            name="name"
            required={true}
            value={name}
            placeholder="Engine CI"
            maxLength={100}
            onChange={(e) => setName(e.target.value)}
            description="A name that says where the key is used, e.g. 'Game CI (dev server)'."
          />
          <Button disabled={name.trim().length === 0}>Create</Button>
        </div>
      </form>
    </div>
  );
}

function Uploads() {
  const [expanded, setExpanded] = useState<string | null>(null);

  const { isLoading, isError, data, refetch } = useQuery({
    queryKey: ["symbol-uploads"],
    queryFn: () => api.get<UploadsResponse>("/_symbols/uploads"),
  });

  const deleteUpload = async (upload: SymbolUpload) => {
    if (!window.confirm(`Delete '${upload.name}'? Crash reports that need these symbols can't be symbolicated anymore.`))
      return;

    await api.delete(`/_symbols/uploads/${upload.id}`);
    refetch();
  };

  if (isLoading) return <LoadingState />;
  if (isError || !data) return <ErrorState />;

  return (
    <div>
      <div className="flex items-center space-x-2 mb-4">
        <IconFileCode className="h-5 w-5 text-foreground" />
        <h3 className="text-lg font-medium">Uploaded symbols</h3>
      </div>

      {!data.available && (
        <Alert variant="warning" className="max-w-[48rem] mb-4">
          <IconAlertTriangle className="h-4 w-4" />
          <AlertTitle>Symbolication is unavailable</AlertTitle>
          <AlertDescription>{data.unavailableMessage}</AlertDescription>
        </Alert>
      )}

      {data.uploads.length === 0 ? (
        <p className="text-sm text-muted-foreground">No symbols uploaded yet.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b text-left text-muted-foreground">
                <th className="py-2 pr-4 font-medium">Archive</th>
                <th className="py-2 pr-4 font-medium">Build</th>
                <th className="py-2 pr-4 font-medium">Platforms</th>
                <th className="py-2 pr-4 font-medium text-right">Files</th>
                <th className="py-2 pr-4 font-medium text-right">Size</th>
                <th className="py-2 pr-4 font-medium">Uploaded</th>
                <th className="py-2" />
              </tr>
            </thead>
            <tbody>
              {data.uploads.map((upload) => {
                const isExpanded = expanded === upload.id;
                const platforms = [...new Set(upload.files.map((f) => f.rid ?? platformNames[f.format] ?? f.format))];
                return (
                  <Fragment key={upload.id}>
                    <tr className="border-b hover:bg-accent/50 cursor-pointer" onClick={() => setExpanded(isExpanded ? null : upload.id)}>
                      <td className="py-2 pr-4">
                        <div className="flex items-center gap-1">
                          {isExpanded ? <IconChevronDown className="w-4 h-4" /> : <IconChevronRight className="w-4 h-4" />}
                          <span className="truncate">{upload.name}</span>
                        </div>
                      </td>
                      <td className="py-2 pr-4 font-mono text-xs">{upload.buildId ?? <span className="text-muted-foreground">engine</span>}</td>
                      <td className="py-2 pr-4">
                        <div className="flex flex-wrap gap-1">
                          {platforms.map((p) => (
                            <Badge key={p} variant="secondary" className="whitespace-nowrap">
                              {p}
                            </Badge>
                          ))}
                        </div>
                      </td>
                      <td className="py-2 pr-4 text-right">{upload.files.length}</td>
                      <td className="py-2 pr-4 text-right whitespace-nowrap">{formatBytes(upload.sizeBytes)}</td>
                      <td className="py-2 pr-4">
                        <div className="whitespace-nowrap">{formatDate(upload.createdAt)}</div>
                        {upload.keyName && (
                          <div className="text-xs text-muted-foreground truncate max-w-[12rem]" title={upload.keyName}>
                            {upload.keyName}
                          </div>
                        )}
                      </td>
                      <td className="py-2 text-right">
                        <Button
                          variant="ghost"
                          size="xs"
                          onClick={(e) => {
                            e.stopPropagation();
                            deleteUpload(upload);
                          }}
                        >
                          <IconTrash className="w-4 h-4" />
                        </Button>
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr className="border-b bg-muted/40">
                        <td colSpan={7} className="py-2 px-6">
                          <ul className="space-y-1 text-xs">
                            {upload.files.map((file) => (
                              <li key={file.debugId} className="flex flex-wrap gap-x-3">
                                <span className="font-mono">{file.path}</span>
                                <span className="text-muted-foreground">
                                  {formatNames[file.format] ?? file.format}
                                  {file.arch && ` ${file.arch}`} · {formatBytes(file.sizeBytes)}
                                </span>
                                <span className="font-mono text-muted-foreground">ID {file.debugId}</span>
                              </li>
                            ))}
                          </ul>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export function DebugSymbols() {
  const uploadUrl = `${window.location.origin}/api/v0/symbols`;

  return (
    <div className="space-y-8">
      <UploadKeys />
      <Uploads />

      <Alert className="max-w-[48rem]">
        <IconHelp className="h-4 w-4" />
        <AlertTitle>How do symbols get here?</AlertTitle>
        <AlertDescription className="text-muted-foreground space-y-2">
          <p>
            Native crash reports and C# exceptions from NativeAOT builds only contain addresses. The server turns them
            back into function names and source lines with the debug symbols of the exact build that crashed.
          </p>
          <p>The engine and game CI (or the publish script) upload their symbol archives with an upload key:</p>
          <pre className="bg-muted p-3 rounded-md text-xs font-mono overflow-x-auto whitespace-pre">
            {`curl --fail -X POST -H "Authorization: Bearer $SYMBOLS_KEY" \\
  --data-binary @Game-linux-symbols.tar.gz \\
  "${uploadUrl}?buildId=$BUILD_ID&name=Game-linux-symbols.tar.gz"`}
          </pre>
          <ul className="list-disc mx-4 space-y-1">
            <li>
              Game archives need <code>buildId</code>, the first part of the <code>Build:</code> line in their reports.
              Engine archives don't.
            </li>
            <li>Symbols are matched by their build ID, PDB ID or UUID, so uploading the same files twice is harmless.</li>
            <li>The symbol store and its keys are shared by every app on this server.</li>
          </ul>
        </AlertDescription>
      </Alert>
    </div>
  );
}
