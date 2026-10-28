namespace Aptabase.Features.Symbolication.Store;

public record IndexedSymbolFile(ExtractedFile File, SymbolIdentity Identity, string? Rid);

public record IndexResult(IReadOnlyList<IndexedSymbolFile> Indexed, IReadOnlyList<SkippedFile> Skipped);

// Decides which files of an unpacked archive are symbol files, and by which debug IDs crash reports find them
public class SymbolIndexer
{
    private static readonly string[] DebugFileSuffixes = [".dbg", ".debug", ".debugsymbols"];

    private readonly SymbolFileInspector _inspector;

    public SymbolIndexer(SymbolFileInspector inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public async Task<IndexResult> IndexAsync(IReadOnlyList<ExtractedFile> files, CancellationToken cancellationToken)
    {
        var indexed = new List<IndexedSymbolFile>();
        var skipped = new List<SkippedFile>();

        foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            var inspection = await _inspector.InspectAsync(file.FullPath, file.RelativePath, cancellationToken);
            if (inspection.Identities.Count == 0)
            {
                skipped.Add(new SkippedFile(file.RelativePath, inspection.SkipReason ?? "Not a symbol file"));
                continue;
            }

            foreach (var identity in inspection.Identities)
                indexed.Add(new IndexedSymbolFile(file, identity, ResolveRid(file.RelativePath, identity)));
        }

        // Two files with the same ID in one archive (e.g. an unstripped library next to its .dbg): keep the debug file
        var kept = new List<IndexedSymbolFile>();
        foreach (var group in indexed.GroupBy(i => i.Identity.DebugId))
        {
            var best = group.OrderByDescending(i => IsDebugFile(i.File.RelativePath)).ThenByDescending(i => i.File.Size).First();
            kept.Add(best);
            skipped.AddRange(group.Where(i => i != best).Select(i => new SkippedFile(i.File.RelativePath, $"Same ID as {best.File.RelativePath}")));
        }

        return new IndexResult(kept, skipped);
    }

    // A Mach-O slice says which architecture it is, otherwise a folder named after a RID (as in the game's archives) decides
    public static string? ResolveRid(string relativePath, SymbolIdentity identity)
    {
        if (identity.Format == SymbolFormats.MachO)
            return RuntimeIds.FromArch(identity.Format, identity.Arch);

        var folderRid = relativePath.Split('/')[..^1].LastOrDefault(RuntimeIds.IsRid);
        return folderRid ?? RuntimeIds.FromArch(identity.Format, identity.Arch);
    }

    private static bool IsDebugFile(string path) => DebugFileSuffixes.Any(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase));
}
