using Aptabase.Features.Symbolication.Store;

namespace Aptabase.Features.Symbolication.Engine;

// Re-symbolicates a stack trace with the uploaded symbols. Symbols are matched by the debug IDs in the report's
// Modules list (native frames), or by the game build in its "Build:" line (C# frames), never by file name or version.
public class Symbolicator
{
    private const string GameLibraryExport = "godotsharp_game_main_init";

    private readonly ISymbolStore _store;
    private readonly SymbolStorage _storage;
    private readonly LlvmSymbolizer _symbolizer;
    private readonly FunctionTableCache _functionTables;
    private readonly ILogger _logger;

    public Symbolicator(ISymbolStore store, SymbolStorage storage, LlvmSymbolizer symbolizer, FunctionTableCache functionTables, ILogger<Symbolicator> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _symbolizer = symbolizer ?? throw new ArgumentNullException(nameof(symbolizer));
        _functionTables = functionTables ?? throw new ArgumentNullException(nameof(functionTables));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private class FrameWork(ReportLine line, ResultFrame frame)
    {
        public ReportLine Line { get; } = line;
        public ResultFrame Frame { get; } = frame;
        public string? MissingKey { get; set; }
        public string? TraceName { get; set; }
        public List<SymbolizedLocation> Locations { get; } = [];
    }

    public async Task<SymbolicationResult> SymbolicateAsync(string stackTrace, CancellationToken cancellationToken)
    {
        var report = CrashReportParser.Parse(stackTrace);
        var platform = report.Platform;
        var result = new SymbolicationResult { BuildId = report.BuildId, Rid = report.Rid };

        var work = new List<FrameWork>();
        foreach (var line in report.Lines)
        {
            if (line.Native is null && line.Managed is null)
            {
                result.Lines.Add(new ResultLine { Text = line.Text });
                continue;
            }

            var frame = new ResultFrame();
            result.Lines.Add(new ResultLine { Text = line.Text, Frame = frame });
            work.Add(new FrameWork(line, frame));
        }

        var moduleIds = report.Modules.Values.Select(m => m.DebugId).OfType<string>().Distinct().ToArray();
        var filesById = (await _store.FindByDebugIds(moduleIds, cancellationToken)).ToDictionary(f => f.DebugId);

        StoredSymbolFile? gameFile = null;
        if (report.HasManagedFrames && report.BuildId is not null && report.Rid is not null)
            gameFile = await _store.FindByBuild(report.BuildId, report.Rid, cancellationToken);

        var lookups = new Dictionary<string, (StoredSymbolFile File, List<(FrameWork Work, ulong Address)> Items)>();
        void Enqueue(StoredSymbolFile file, FrameWork item, ulong address)
        {
            if (!lookups.TryGetValue(file.DebugId, out var lookup))
                lookups[file.DebugId] = lookup = (file, []);
            lookup.Items.Add((item, address));
        }

        foreach (var item in work)
        {
            if (item.Line.Native is { } native)
                PrepareNative(item, native, report, filesById, platform, Enqueue);
            else
                await PrepareManaged(item, item.Line.Managed!, report, gameFile, Enqueue, cancellationToken);
        }

        foreach (var (file, items) in lookups.Values)
            await Symbolize(file, items, cancellationToken);

        Finish(result, work, report, filesById, gameFile);
        return result;
    }

    private static void PrepareNative(FrameWork item, NativeFrame native, CrashReport report, Dictionary<string, StoredSymbolFile> filesById,
        ReportPlatform platform, Action<StoredSymbolFile, FrameWork, ulong> enqueue)
    {
        var frame = item.Frame;
        frame.Kind = "native";
        frame.Index = native.Index;
        frame.Module = native.Module;
        frame.Offset = $"{native.Offset:x}";
        frame.CrashHandler = AddressRules.IsCrashHandlerFrame(platform, native.Index);

        if (!report.Modules.TryGetValue(native.Module, out var module))
        {
            frame.Reason = $"{native.Module} isn't in the report's Modules list";
            return;
        }
        if (module.DebugId is null)
        {
            frame.Reason = $"The Modules list has no ID for {native.Module}";
            return;
        }
        if (!filesById.TryGetValue(module.DebugId, out var file))
        {
            item.MissingKey = module.DebugId;
            return;
        }

        enqueue(file, item, AddressRules.NativeAddress(platform, native, (ulong)file.ImageBase));
    }

    private async Task PrepareManaged(FrameWork item, ManagedFrame managed, CrashReport report, StoredSymbolFile? gameFile,
        Action<StoredSymbolFile, FrameWork, ulong> enqueue, CancellationToken cancellationToken)
    {
        var frame = item.Frame;
        frame.Kind = "managed";
        frame.ModuleRole = ModuleRole.Game;
        frame.Module = managed.Module ?? (gameFile is null ? "C#" : FrameFormatter.AssemblyName(gameFile.ModuleName));
        frame.Offset = $"0x{managed.Offset:x}";

        if (report.BuildId is null || report.Rid is null)
        {
            frame.Reason = "The report has no build ID, so the game's symbols can't be found";
            return;
        }
        if (gameFile is null)
        {
            frame.Reason = $"No symbols uploaded for build {report.BuildId} ({report.Rid})";
            item.MissingKey = SymbolStore.BuildKey(report.BuildId, report.Rid);
            return;
        }

        if (managed.MethodName is null)
        {
            enqueue(gameFile, item, AddressRules.ManagedAddress((ulong)gameFile.ImageBase, managed.Offset));
            return;
        }

        var table = await _functionTables.GetAsync(gameFile, cancellationToken);
        var match = ManagedNameMatcher.Find(table, managed.MethodName, managed.Offset);
        if (match.Function is null)
        {
            frame.Reason = match.Reason;
            return;
        }

        item.TraceName = managed.MethodName;
        enqueue(gameFile, item, AddressRules.ManagedAddress(match.Function.Address, managed.Offset));
    }

    private async Task Symbolize(StoredSymbolFile file, List<(FrameWork Work, ulong Address)> items, CancellationToken cancellationToken)
    {
        var path = _storage.FullPath(file.Path);
        var objectPath = file.Format == SymbolFormats.Pdb ? path + PdbStub.FileSuffix : path;
        var arch = file.Format == SymbolFormats.MachO ? file.Arch : null;

        IReadOnlyList<SymbolizedAddress> results;
        try
        {
            results = await _symbolizer.SymbolizeAsync(objectPath, arch, items.Select(i => i.Address).ToArray(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Symbolizing with {Path} failed", file.Path);
            foreach (var (item, _) in items)
                item.Frame.Reason = $"llvm-symbolizer failed: {ex.Message}";
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var (item, _) = items[i];
            if (results[i].Error is { } error)
                item.Frame.Reason = $"llvm-symbolizer failed: {error}";
            else if (results[i].Locations.Count == 0)
                item.Frame.Reason = $"No function at this address in the symbols of {item.Frame.Module}";
            else
                item.Locations.AddRange(results[i].Locations);
        }
    }

    private static void Finish(SymbolicationResult result, List<FrameWork> work, CrashReport report,
        Dictionary<string, StoredSymbolFile> filesById, StoredSymbolFile? gameFile)
    {
        var roles = ModuleRoles(work, report, filesById);

        // Named after the symbol file rather than the report's module, Windows reports write it in lower case (ballsydrop.dll)
        var gameModule = roles.Where(r => r.Value == ModuleRole.Game).Select(r => r.Key).FirstOrDefault();
        var gameModuleFile = gameModule is not null && report.Modules.TryGetValue(gameModule, out var module) && module.DebugId is not null
            ? filesById.GetValueOrDefault(module.DebugId)
            : null;
        var gameAssembly = (gameFile ?? gameModuleFile) is { } symbols
            ? FrameFormatter.AssemblyName(symbols.ModuleName)
            : gameModule is null ? null : FrameFormatter.AssemblyName(gameModule);

        foreach (var item in work)
        {
            var frame = item.Frame;
            if (frame.Kind == "native")
                frame.ModuleRole = roles.GetValueOrDefault(frame.Module, ModuleRole.System);

            if (item.Locations.Count > 0)
            {
                frame.Status = FrameStatus.Resolved;
                frame.Reason = null;
                for (var i = 0; i < item.Locations.Count; i++)
                {
                    var location = item.Locations[i];
                    var outermost = i == item.Locations.Count - 1;
                    var trusted = FrameFormatter.HasTrustedSource(location.FunctionName, location.FileName, gameAssembly);
                    frame.Symbols.Add(new ResultSymbol
                    {
                        Function = outermost && item.TraceName is not null
                            ? ManagedNameMatcher.ShortName(item.TraceName)
                            : FrameFormatter.PrettyFunction(location.FunctionName),
                        RawFunction = location.FunctionName,
                        Location = trusted ? FrameFormatter.Location(location) : null,
                        NoSourceInfo = !trusted || (FrameFormatter.IsManaged(location.FunctionName) && location.FileName is null),
                        Inlined = !outermost,
                    });
                }
                continue;
            }

            // The names the crashing process found itself are only right for system libraries: for the game library
            // they're the nearest export (godotsharp_game_main_init + a huge offset), and the engine has none
            if (item.Line.Native is { } native && frame.ModuleRole == ModuleRole.System && FrameFormatter.ReportedName(native.Reported) is { } name)
            {
                frame.Status = FrameStatus.Named;
                frame.ReportedName = name;
                frame.Reason = null;
                continue;
            }

            frame.Status = FrameStatus.Unresolved;
            if (frame.Reason is null && item.MissingKey is not null)
            {
                frame.Reason = frame.ModuleRole == ModuleRole.System
                    ? $"System library, no symbols uploaded for ID {item.MissingKey}"
                    : $"No symbols uploaded for {frame.Module} (ID {item.MissingKey})";
            }
        }

        // Any later upload of a missing ID re-symbolicates the report, the list shown only has the ones that left frames unresolved
        result.MissingKeys = work.Select(w => w.MissingKey).OfType<string>().Distinct().ToList();
        result.Missing = work
            .Where(w => w.MissingKey is not null && w.Frame.Status == FrameStatus.Unresolved && !w.Frame.CrashHandler)
            .GroupBy(w => w.MissingKey!)
            .Select(g => new MissingSymbols { Key = g.Key, Module = g.First().Frame.Module })
            .ToList();

        var counted = work.Where(w => !w.Frame.CrashHandler).ToArray();
        result.TotalFrames = counted.Length;
        result.ResolvedFrames = counted.Count(w => w.Frame.Status != FrameStatus.Unresolved);
    }

    // The engine executable is always called main. The game library is recognized by resolving to C# code, by having
    // been uploaded as a game build, by the export Godot's C# games have (which macOS reports name frames after), or
    // by having the executable's name: both are named after the project (BallsyDrop.x86_64 and BallsyDrop.so).
    private static Dictionary<string, string> ModuleRoles(List<FrameWork> work, CrashReport report, Dictionary<string, StoredSymbolFile> filesById)
    {
        var executable = report.Modules.TryGetValue("main", out var main) ? FrameFormatter.AssemblyName(main.File) : null;
        var roles = new Dictionary<string, string>();
        foreach (var group in work.Where(w => w.Line.Native is not null).GroupBy(w => w.Frame.Module))
        {
            var module = group.Key;
            var file = report.Modules.TryGetValue(module, out var m) && m.DebugId is not null ? filesById.GetValueOrDefault(m.DebugId) : null;
            var isGame = file?.BuildId is not null
                || group.Any(w => w.Locations.Count > 0 && FrameFormatter.IsManaged(w.Locations[^1].FunctionName))
                || group.Any(w => w.Line.Native!.Reported?.Contains(GameLibraryExport, StringComparison.Ordinal) ?? false)
                || (executable is not null && string.Equals(FrameFormatter.AssemblyName(module), executable, StringComparison.OrdinalIgnoreCase));

            roles[module] = module == "main" ? ModuleRole.Engine : isGame ? ModuleRole.Game : ModuleRole.System;
        }
        return roles;
    }
}
