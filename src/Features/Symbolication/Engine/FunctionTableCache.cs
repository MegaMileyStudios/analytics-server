using Aptabase.Features.Symbolication.Store;
using Microsoft.Extensions.Caching.Memory;

namespace Aptabase.Features.Symbolication.Engine;

// Function tables are only needed for the game's symbols (C# frames with method names). They're built the first time
// they're needed and kept as a <file>.functions.tsv file next to the symbols, and in memory for a while.
public class FunctionTableCache
{
    private const string SidecarSuffix = ".functions.tsv";
    private static readonly TimeSpan MemoryLifetime = TimeSpan.FromMinutes(10);

    private readonly LlvmTools _tools;
    private readonly SymbolStorage _storage;
    private readonly IMemoryCache _cache;

    public FunctionTableCache(LlvmTools tools, SymbolStorage storage, IMemoryCache cache)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<FunctionTable> GetAsync(StoredSymbolFile file, CancellationToken cancellationToken)
    {
        var cacheKey = $"SYMBOL-FUNCTIONS-{file.DebugId}";
        if (_cache.TryGetValue(cacheKey, out FunctionTable? cached) && cached is not null)
            return cached;

        var path = _storage.FullPath(file.Path);
        var sidecar = $"{path}.{file.DebugId}{SidecarSuffix}";
        FunctionTable table;
        if (File.Exists(sidecar))
        {
            table = FunctionTable.Load(sidecar);
        }
        else
        {
            table = await BuildAsync(file, path, cancellationToken);
            table.Save(sidecar);
        }

        _cache.Set(cacheKey, table, new MemoryCacheEntryOptions { SlidingExpiration = MemoryLifetime });
        return table;
    }

    private async Task<FunctionTable> BuildAsync(StoredSymbolFile file, string path, CancellationToken cancellationToken)
    {
        switch (file.Format)
        {
            case SymbolFormats.Pdb:
                var dump = await _tools.RunAsync(LlvmTools.PdbUtil, ["dump", "--publics", "--section-headers", path], cancellationToken);
                return FunctionTable.ParsePdbDump(dump, (ulong)file.ImageBase);

            case SymbolFormats.MachO:
                var machO = await _tools.RunAsync(LlvmTools.Nm, ["-m", "--defined-only", "--no-demangle", $"--arch={file.Arch}", path], cancellationToken);
                return FunctionTable.ParseMachONm(machO);

            default:
                var elf = await _tools.RunAsync(LlvmTools.Nm, ["--defined-only", "--print-size", "--no-demangle", path], cancellationToken);
                return FunctionTable.ParseElfNm(elf);
        }
    }
}
