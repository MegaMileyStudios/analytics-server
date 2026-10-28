using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Aptabase.Features.Symbolication.Store;

public record ExtractedFile(string RelativePath, string FullPath, long Size);

public record SkippedFile(string Path, string Reason);

public record ExtractionResult(IReadOnlyList<ExtractedFile> Files, IReadOnlyList<SkippedFile> Skipped);

// Thrown for uploads the server refuses, the message is returned to the uploader
public class SymbolUploadException : Exception
{
    public SymbolUploadException(string message, Exception? inner = null) : base(message, inner) { }
}

// Unpacks a .tar.gz symbol archive while it is being uploaded, so the archive itself is never stored
public static partial class SymbolArchiveExtractor
{
    public static async Task<ExtractionResult> ExtractAsync(Stream archive, string targetDirectory, long maxTotalBytes, int maxEntries, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, ExtractedFile>();
        var skipped = new List<SkippedFile>();
        long totalBytes = 0;
        var entries = 0;

        try
        {
            await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
            var reader = new TarStreamReader(gzip);

            while (await reader.NextAsync(cancellationToken) is { } entry)
            {
                if (++entries > maxEntries)
                    throw new SymbolUploadException($"The archive has more than {maxEntries} entries");

                if (entry.Kind == TarEntryKind.Directory)
                    continue;

                var path = SanitizeEntryPath(entry.Path, out var skipReason);
                if (path is null)
                {
                    if (skipReason is not null)
                        skipped.Add(new SkippedFile(entry.Path, skipReason));
                    continue;
                }

                if (entry.Kind != TarEntryKind.File)
                {
                    skipped.Add(new SkippedFile(path, "Not a regular file"));
                    continue;
                }

                totalBytes += entry.Size;
                if (totalBytes > maxTotalBytes)
                    throw new SymbolUploadException($"The archive unpacks to more than {maxTotalBytes / (1024 * 1024)} MB");

                var fullPath = Path.Combine(targetDirectory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await using (var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    await reader.CopyDataToAsync(file, cancellationToken);
                files[path] = new ExtractedFile(path, fullPath, entry.Size);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or EndOfStreamException)
        {
            throw new SymbolUploadException("The upload isn't a valid .tar.gz archive", ex);
        }

        return new ExtractionResult(files.Values.ToArray(), skipped);
    }

    // Returns the entry's path relative to the archive root, or null when it must not be extracted.
    // skipReason is null for entries that are silently ignored (the archive root and macOS metadata).
    public static string? SanitizeEntryPath(string entryName, out string? skipReason)
    {
        skipReason = null;
        var name = entryName.Replace('\\', '/');
        if (name.StartsWith('/') || WindowsDriveRegex().IsMatch(name))
        {
            skipReason = "Absolute paths aren't allowed";
            return null;
        }

        var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToArray();
        if (segments.Length == 0)
            return null;

        if (segments.Contains(".."))
        {
            skipReason = "Paths outside the archive aren't allowed";
            return null;
        }

        // tar on macOS adds AppleDouble files (._<name>) with extended attributes next to every file, they're ignored silently
        if (segments[^1].StartsWith("._") || segments[^1] == ".DS_Store" || segments.Contains("__MACOSX"))
            return null;

        return string.Join('/', segments);
    }

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex WindowsDriveRegex();
}
