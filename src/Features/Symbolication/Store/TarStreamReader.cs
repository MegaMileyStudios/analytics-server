using System.Globalization;
using System.Text;

namespace Aptabase.Features.Symbolication.Store;

public enum TarEntryKind
{
    File,
    Directory,
    Other,
}

public record TarStreamEntry(string Path, TarEntryKind Kind, long Size);

// Reads a tar stream entry by entry, without seeking. System.Formats.Tar can't be used: for pax entries it ignores
// the ustar prefix field, which bsdtar (macOS) uses for paths over 100 characters, e.g. the DWARF file in the
// engine's dSYM archive. Supports ustar (with prefix), pax extended headers (path, size) and GNU long names.
public sealed class TarStreamReader
{
    private const int BlockSize = 512;
    private const int MaxHeaderDataSize = 1024 * 1024;

    private readonly Stream _stream;
    private readonly byte[] _block = new byte[BlockSize];
    private long _remaining;
    private long _padding;

    public TarStreamReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    // Returns null at the end of the archive. Data the caller didn't read of the previous entry is skipped.
    public async Task<TarStreamEntry?> NextAsync(CancellationToken cancellationToken)
    {
        await SkipAsync(_remaining + _padding, cancellationToken);
        _remaining = _padding = 0;

        Dictionary<string, string>? pax = null;
        string? longName = null;
        while (true)
        {
            if (!await ReadBlockAsync(cancellationToken) || _block.All(b => b == 0))
                return null;

            VerifyChecksum();
            var type = (char)_block[156];
            var size = ParseSize(_block.AsSpan(124, 12));

            switch (type)
            {
                case 'x':
                    pax = ParsePax(await ReadHeaderDataAsync(size, cancellationToken));
                    continue;
                case 'L':
                    longName = Encoding.UTF8.GetString(await ReadHeaderDataAsync(size, cancellationToken)).TrimEnd('\0');
                    continue;
                case 'g' or 'K':
                    await ReadHeaderDataAsync(size, cancellationToken);
                    continue;
            }

            if (pax?.TryGetValue("size", out var paxSize) == true)
                size = long.Parse(paxSize, CultureInfo.InvariantCulture);

            var path = pax?.GetValueOrDefault("path") ?? longName ?? UstarPath();
            var kind = type switch
            {
                '0' or '\0' or '7' => TarEntryKind.File,
                '5' => TarEntryKind.Directory,
                _ => TarEntryKind.Other,
            };

            // Only regular files carry data (hard links, symlinks and directories have a size of 0)
            _remaining = kind == TarEntryKind.File ? size : 0;
            _padding = PaddingFor(_remaining);
            return new TarStreamEntry(path, kind, _remaining);
        }
    }

    // Copies the current entry's data
    public async Task CopyDataToAsync(Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (_remaining > 0)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, _remaining)), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("The archive ends in the middle of a file");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            _remaining -= read;
        }
    }

    private string UstarPath()
    {
        var name = ReadString(_block.AsSpan(0, 100));
        var isUstar = _block.AsSpan(257, 5).SequenceEqual("ustar"u8);
        var prefix = isUstar ? ReadString(_block.AsSpan(345, 155)) : "";
        return prefix.Length > 0 ? $"{prefix}/{name}" : name;
    }

    private void VerifyChecksum()
    {
        var expected = ParseOctal(_block.AsSpan(148, 8));
        long sum = 0;
        for (var i = 0; i < BlockSize; i++)
            sum += i is >= 148 and < 156 ? ' ' : _block[i];

        if (sum != expected)
            throw new InvalidDataException("Invalid tar header checksum");
    }

    private async Task<bool> ReadBlockAsync(CancellationToken cancellationToken)
    {
        var read = await _stream.ReadAtLeastAsync(_block, BlockSize, throwOnEndOfStream: false, cancellationToken);
        if (read == 0)
            return false;
        if (read < BlockSize)
            throw new EndOfStreamException("The archive ends in the middle of a header");
        return true;
    }

    private async Task<byte[]> ReadHeaderDataAsync(long size, CancellationToken cancellationToken)
    {
        if (size > MaxHeaderDataSize)
            throw new InvalidDataException("Tar extended header is too large");

        var data = new byte[size];
        await _stream.ReadExactlyAsync(data, cancellationToken);
        await SkipAsync(PaddingFor(size), cancellationToken);
        return data;
    }

    private async Task SkipAsync(long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (count > 0)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("The archive ends in the middle of a file");
            count -= read;
        }
    }

    // Records are "<length> <key>=<value>\n", the length counting the whole record
    private static Dictionary<string, string> ParsePax(byte[] data)
    {
        var records = new Dictionary<string, string>();
        var position = 0;
        while (position < data.Length)
        {
            var space = Array.IndexOf(data, (byte)' ', position);
            if (space < 0 || !int.TryParse(Encoding.ASCII.GetString(data, position, space - position), CultureInfo.InvariantCulture, out var length) || length <= 0 || position + length > data.Length)
                throw new InvalidDataException("Invalid pax extended header");

            var record = Encoding.UTF8.GetString(data, space + 1, position + length - space - 2);
            var equals = record.IndexOf('=');
            if (equals > 0)
                records[record[..equals]] = record[(equals + 1)..];
            position += length;
        }
        return records;
    }

    // Octal, or base-256 (high bit set) for files of 8 GB and more
    private static long ParseSize(ReadOnlySpan<byte> field)
    {
        if ((field[0] & 0x80) == 0)
            return ParseOctal(field);

        long value = field[0] & 0x7F;
        foreach (var b in field[1..])
            value = (value << 8) | b;
        return value;
    }

    private static long ParseOctal(ReadOnlySpan<byte> field)
    {
        var text = Encoding.ASCII.GetString(field).Trim('\0', ' ');
        if (text.Length == 0)
            return 0;
        if (!text.All(c => c is >= '0' and <= '7'))
            throw new InvalidDataException("Invalid number in tar header");
        return Convert.ToInt64(text, 8);
    }

    private static string ReadString(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? field : field[..end]);
    }

    private static long PaddingFor(long size) => (BlockSize - size % BlockSize) % BlockSize;
}
