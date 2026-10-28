using System.Globalization;
using System.Text.RegularExpressions;

namespace Aptabase.Features.Symbolication.Store;

public static class SymbolFormats
{
    public const string Elf = "elf";
    public const string Pdb = "pdb";
    public const string MachO = "macho";
}

// One debug ID a symbol file can be found by. A universal Mach-O file has one per architecture.
// ImageBase is what has to be added to a module offset to get the address llvm-symbolizer expects.
public record SymbolIdentity(string DebugId, string Format, string? Arch, ulong ImageBase);

// SkipReason says why a file without identities isn't used
public record SymbolInspection(IReadOnlyList<SymbolIdentity> Identities, string? SkipReason)
{
    public static SymbolInspection Skip(string reason) => new([], reason);
}

// Reads the IDs that crash reports refer to (GNU build ID, PDB GUID + age, Mach-O UUID) from symbol files
public partial class SymbolFileInspector
{
    private static readonly byte[] ElfMagic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];
    private static readonly byte[] PdbMagic = "Microsoft C/C++ MSF 7.00"u8.ToArray();

    private readonly LlvmTools _tools;

    public SymbolFileInspector(LlvmTools tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    // Debug IDs are compared without dashes and in upper case, they are written differently per platform and tool
    public static string NormalizeDebugId(string id) => id.Replace("-", "").ToUpperInvariant();

    // dSYM bundles keep their DWARF in <name>.dSYM/Contents/Resources/DWARF/<name>. The .dylib or executable next
    // to the bundle must not be used: its symbol table gives the nearest exported symbol, not the real function.
    public static bool IsDsymDwarfPath(string relativePath)
    {
        var parts = relativePath.Split('/');
        return parts.Length >= 5
            && parts[^2] == "DWARF" && parts[^3] == "Resources" && parts[^4] == "Contents"
            && parts[^5].EndsWith(".dSYM", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SymbolInspection> InspectAsync(string path, string relativePath, CancellationToken cancellationToken)
    {
        var header = await ReadHeader(path, cancellationToken);

        if (IsDsymDwarfPath(relativePath))
            return await InspectMachO(path, cancellationToken);

        if (relativePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && header.AsSpan().StartsWith(PdbMagic))
            return await InspectPdb(path, cancellationToken);

        if (header.AsSpan().StartsWith(ElfMagic))
            return await InspectElf(path, header, cancellationToken);

        if (relativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return SymbolInspection.Skip("Only the module's .pdb is needed");

        if (IsMachO(header))
            return SymbolInspection.Skip("Only the DWARF file inside the .dSYM bundle is used");

        return SymbolInspection.Skip("Not a symbol file");
    }

    private async Task<SymbolInspection> InspectElf(string path, byte[] header, CancellationToken cancellationToken)
    {
        // A stripped library has the same build ID as its debug file, but only its exported symbols, which give misleading names
        var sections = await _tools.RunAsync(LlvmTools.ReadElf, ["-S", "-W", path], cancellationToken);
        if (!HasElfDebugInfo(sections))
            return SymbolInspection.Skip("No debug info (a stripped binary)");

        var notes = await _tools.RunAsync(LlvmTools.ReadElf, ["-n", path], cancellationToken);
        var buildId = ParseElfBuildId(notes);
        if (buildId is null)
            return SymbolInspection.Skip("No GNU build ID");

        return new([new SymbolIdentity(NormalizeDebugId(buildId), SymbolFormats.Elf, ElfArch(header), 0)], null);
    }

    private async Task<SymbolInspection> InspectPdb(string path, CancellationToken cancellationToken)
    {
        var summary = await _tools.RunAsync(LlvmTools.PdbUtil, ["dump", "--summary", path], cancellationToken);
        var identity = ParsePdbSummary(summary);
        if (identity is null)
            return SymbolInspection.Skip("No GUID and age in the PDB");

        return new([new SymbolIdentity(PdbDebugId(identity.Value.Guid, identity.Value.Age), SymbolFormats.Pdb, null, PdbStub.ImageBase)], null);
    }

    private async Task<SymbolInspection> InspectMachO(string path, CancellationToken cancellationToken)
    {
        var uuids = ParseDwarfUuids(await _tools.RunAsync(LlvmTools.DwarfDump, ["--uuid", path], cancellationToken));
        if (uuids.Count == 0)
            return SymbolInspection.Skip("No UUID in the DWARF file");

        var identities = new List<SymbolIdentity>();
        foreach (var (uuid, arch) in uuids)
        {
            var headers = await _tools.RunAsync(LlvmTools.ObjDump, ["--macho", "--private-headers", $"--arch={arch}", path], cancellationToken);
            identities.Add(new SymbolIdentity(NormalizeDebugId(uuid), SymbolFormats.MachO, arch, ParseMachOTextVmAddr(headers)));
        }
        return new(identities, null);
    }

    private static bool IsMachO(byte[] header)
    {
        if (header.Length < 4)
            return false;

        var magic = BitConverter.ToUInt32(header, 0);
        return magic is 0xFEEDFACF or 0xFEEDFACE or 0xBEBAFECA;
    }

    // The ID a Windows crash report prints: the GUID as 32 hex characters followed by the age in hex
    public static string PdbDebugId(Guid guid, int age) => $"{guid:N}{age:X}".ToUpperInvariant();

    public static string? ParseElfBuildId(string readelfNotes)
    {
        var match = ElfBuildIdRegex().Match(readelfNotes);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool HasElfDebugInfo(string readelfSections) => ElfDebugInfoRegex().IsMatch(readelfSections);

    public static (Guid Guid, int Age)? ParsePdbSummary(string summary)
    {
        var guid = PdbGuidRegex().Match(summary);
        var age = PdbAgeRegex().Match(summary);
        if (!guid.Success || !age.Success)
            return null;

        return (Guid.Parse(guid.Groups[1].Value), int.Parse(age.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    public static IReadOnlyList<(string Uuid, string Arch)> ParseDwarfUuids(string dwarfdumpOutput)
    {
        return MachOUuidRegex().Matches(dwarfdumpOutput).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToArray();
    }

    // 0 for a .dylib, 0x100000000 for an executable
    public static ulong ParseMachOTextVmAddr(string objdumpPrivateHeaders)
    {
        var match = MachOTextVmAddrRegex().Match(objdumpPrivateHeaders);
        return match.Success ? ulong.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
    }

    public static string? ElfArch(byte[] header)
    {
        if (header.Length < 20)
            return null;

        return BitConverter.ToUInt16(header, 18) switch
        {
            0x3E => "x86_64",
            0xB7 => "aarch64",
            _ => null,
        };
    }

    private static async Task<byte[]> ReadHeader(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[64];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        return buffer[..read];
    }

    [GeneratedRegex(@"Build ID: ([0-9a-fA-F]+)")]
    private static partial Regex ElfBuildIdRegex();

    [GeneratedRegex(@"\]\s+\.z?debug_info\s")]
    private static partial Regex ElfDebugInfoRegex();

    [GeneratedRegex(@"GUID: \{([0-9A-Fa-f-]+)\}")]
    private static partial Regex PdbGuidRegex();

    [GeneratedRegex(@"^\s*Age: (\d+)", RegexOptions.Multiline)]
    private static partial Regex PdbAgeRegex();

    [GeneratedRegex(@"UUID: ([0-9A-Fa-f-]+) \((\w+)\)")]
    private static partial Regex MachOUuidRegex();

    [GeneratedRegex(@"segname __TEXT\s+vmaddr 0x([0-9a-fA-F]+)")]
    private static partial Regex MachOTextVmAddrRegex();
}
