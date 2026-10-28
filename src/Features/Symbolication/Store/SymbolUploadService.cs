using Aptabase.Data;

namespace Aptabase.Features.Symbolication.Store;

public record UploadedSymbolFile(string Path, string Format, string? Arch, string? Rid, string DebugId);

public record UploadSummary(string? UploadId, UploadedSymbolFile[] Files, UploadedSymbolFile[] AlreadyStored, SkippedFile[] Skipped);

public class SymbolUploadService
{
    // The largest archives (macOS engine dSYM) unpack to about 600 MB
    private const long MaxExtractedBytes = 16L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntries = 20_000;

    // One upload at a time: they are large and CPU/disk heavy, and debug IDs must be checked and stored atomically
    private static readonly SemaphoreSlim _uploadLock = new(1, 1);

    private readonly ISymbolStore _store;
    private readonly SymbolStorage _storage;
    private readonly SymbolIndexer _indexer;
    private readonly ILogger _logger;

    public SymbolUploadService(ISymbolStore store, SymbolStorage storage, SymbolIndexer indexer, ILogger<SymbolUploadService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UploadSummary> UploadAsync(Stream archive, string name, string? buildId, string keyId, CancellationToken cancellationToken)
    {
        await _uploadLock.WaitAsync(cancellationToken);
        var uploadId = NanoId.New();
        var staging = "";
        try
        {
            staging = _storage.CreateStagingDirectory(uploadId);
            var extraction = await SymbolArchiveExtractor.ExtractAsync(archive, staging, MaxExtractedBytes, MaxArchiveEntries, cancellationToken);
            var index = await _indexer.IndexAsync(extraction.Files, cancellationToken);
            var skipped = extraction.Skipped.Concat(index.Skipped).ToArray();

            var existingIds = (await _store.FindByDebugIds(index.Indexed.Select(i => i.Identity.DebugId).ToArray(), cancellationToken))
                .Select(f => f.DebugId)
                .ToHashSet();
            var alreadyStored = index.Indexed.Where(i => existingIds.Contains(i.Identity.DebugId)).ToArray();
            var newFiles = index.Indexed.Where(i => !existingIds.Contains(i.Identity.DebugId)).ToArray();

            if (buildId is not null)
                await _store.AssignBuildId(alreadyStored.Select(i => i.Identity.DebugId).ToArray(), buildId, cancellationToken);

            if (newFiles.Length == 0)
            {
                if (alreadyStored.Length == 0)
                    throw new SymbolUploadException("The archive contains no symbol files");

                return new UploadSummary(null, [], alreadyStored.Select(ToSummary).ToArray(), skipped);
            }

            foreach (var pdb in newFiles.Where(f => f.Identity.Format == SymbolFormats.Pdb))
                await WritePdbStub(pdb, cancellationToken);

            // Only symbol files are kept, e.g. the stripped library and .dll next to them are deleted
            var keep = newFiles.Select(f => f.File.RelativePath).ToHashSet();
            foreach (var file in extraction.Files.Where(f => !keep.Contains(f.RelativePath)))
                File.Delete(file.FullPath);
            DeleteEmptyDirectories(staging);

            _storage.MoveIntoPlace(staging, uploadId);

            var rows = newFiles.Select(f => new NewSymbolFile(
                f.Identity.DebugId,
                f.Identity.Format,
                f.Identity.Arch,
                f.Rid,
                Path.GetFileName(f.File.RelativePath),
                $"{uploadId}/{f.File.RelativePath}",
                f.Identity.ImageBase,
                f.File.Size)).ToArray();
            var sizeBytes = newFiles.DistinctBy(f => f.File.RelativePath).Sum(f => f.File.Size);
            try
            {
                await _store.SaveUpload(uploadId, name, buildId, keyId, sizeBytes, rows, cancellationToken);
            }
            catch
            {
                _storage.DeleteUpload(uploadId);
                throw;
            }

            _logger.LogInformation("Stored symbol upload {UploadId} ({Name}) with {Count} files", uploadId, name, rows.Length);
            return new UploadSummary(uploadId, newFiles.Select(ToSummary).ToArray(), alreadyStored.Select(ToSummary).ToArray(), skipped);
        }
        finally
        {
            if (staging != "")
                _storage.DeleteDirectory(staging);
            _uploadLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string uploadId, CancellationToken cancellationToken)
    {
        var deleted = await _store.DeleteUpload(uploadId, cancellationToken);
        if (deleted)
            _storage.DeleteUpload(uploadId);
        return deleted;
    }

    // llvm-symbolizer finds the PDB by the file name in the stub's CodeView record, so the stub sits right next to it
    private static async Task WritePdbStub(IndexedSymbolFile pdb, CancellationToken cancellationToken)
    {
        var guid = Guid.ParseExact(pdb.Identity.DebugId[..32], "N");
        var age = Convert.ToInt32(pdb.Identity.DebugId[32..], 16);
        var stub = PdbStub.Build(guid, age, Path.GetFileName(pdb.File.FullPath));
        await File.WriteAllBytesAsync(pdb.File.FullPath + PdbStub.FileSuffix, stub, cancellationToken);
    }

    private static void DeleteEmptyDirectories(string root)
    {
        foreach (var directory in Directory.GetDirectories(root))
        {
            DeleteEmptyDirectories(directory);
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    private static UploadedSymbolFile ToSummary(IndexedSymbolFile file) =>
        new(file.File.RelativePath, file.Identity.Format, file.Identity.Arch, file.Rid, file.Identity.DebugId);
}
