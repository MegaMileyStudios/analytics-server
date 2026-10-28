using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Aptabase.Features.Symbolication.Engine;

// Address is what llvm-symbolizer expects for the function's first instruction (the image base already added)
public record FunctionSymbol(ulong Address, ulong Size, string Name);

// The functions in a symbol file with their addresses and sizes, used to find the managed methods named in a stack trace
public partial class FunctionTable
{
    public IReadOnlyList<FunctionSymbol> Functions { get; }

    // The names without generic instantiations, which is what managed names are matched on
    public IReadOnlyList<string> MatchNames => _matchNames.Value;

    private readonly Lazy<string[]> _matchNames;

    public FunctionTable(IEnumerable<FunctionSymbol> functions)
    {
        // Sizes that are unknown (Mach-O has none, PDB publics neither) end where the next function starts
        var sorted = functions.OrderBy(f => f.Address).ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            if (sorted[i].Size == 0 && i + 1 < sorted.Length)
                sorted[i] = sorted[i] with { Size = sorted[i + 1].Address - sorted[i].Address };
        }
        Functions = sorted;
        _matchNames = new Lazy<string[]>(() => sorted.Select(f => ManagedNameMatcher.RemoveGenericArguments(f.Name)).ToArray());
    }

    // llvm-nm --defined-only --print-size --no-demangle on an ELF file: "<address> [<size>] <type> <name>"
    public static FunctionTable ParseElfNm(string output)
    {
        var functions = new List<FunctionSymbol>();
        foreach (var line in output.Split('\n'))
        {
            var match = ElfNmLineRegex().Match(line);
            if (!match.Success || !"tTwW".Contains(match.Groups["type"].Value[0]))
                continue;

            var size = match.Groups["size"].Success ? ParseHex(match.Groups["size"].Value) : 0;
            functions.Add(new FunctionSymbol(ParseHex(match.Groups["address"].Value), size, match.Groups["name"].Value));
        }
        return new FunctionTable(functions);
    }

    // llvm-nm -m --defined-only --no-demangle on a Mach-O file: "<address> (<segment>,<section>) <flags> _<name>".
    // Only code (__TEXT) counts, NativeAOT puts unwind data with the same names in __DATA.
    public static FunctionTable ParseMachONm(string output)
    {
        var functions = new List<FunctionSymbol>();
        foreach (var line in output.Split('\n'))
        {
            var match = MachONmLineRegex().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value;
            functions.Add(new FunctionSymbol(ParseHex(match.Groups["address"].Value), 0, name.StartsWith('_') ? name[1..] : name));
        }
        return new FunctionTable(functions);
    }

    // llvm-pdbutil dump --publics --section-headers: public symbols are "section:offset", the code sections' addresses
    // come from the section headers. The PDB's addresses are relative, so the stand-in PE's image base is added.
    public static FunctionTable ParsePdbDump(string output, ulong imageBase)
    {
        var sectionAddresses = new Dictionary<int, ulong>();
        var codeSections = new HashSet<int>();
        var publics = new List<(string Name, int Section, ulong Offset)>();

        var section = 0;
        string? pendingPublic = null;
        foreach (var line in output.Split('\n'))
        {
            if (SectionHeaderRegex().Match(line) is { Success: true } header)
            {
                section = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }
            if (section > 0 && SectionAddressRegex().Match(line) is { Success: true } address)
            {
                sectionAddresses[section] = ParseHex(address.Groups[1].Value);
                continue;
            }
            if (section > 0 && line.Contains("IMAGE_SCN_CNT_CODE"))
            {
                codeSections.Add(section);
                continue;
            }
            if (PublicNameRegex().Match(line) is { Success: true } name)
            {
                pendingPublic = name.Groups[1].Value;
                continue;
            }
            if (pendingPublic is not null && PublicAddressRegex().Match(line) is { Success: true } location)
            {
                publics.Add((pendingPublic,
                    int.Parse(location.Groups[1].Value, CultureInfo.InvariantCulture),
                    ulong.Parse(location.Groups[2].Value, CultureInfo.InvariantCulture)));
                pendingPublic = null;
            }
        }

        var functions = publics
            .Where(p => codeSections.Contains(p.Section) && sectionAddresses.ContainsKey(p.Section))
            .Select(p => new FunctionSymbol(imageBase + sectionAddresses[p.Section] + p.Offset, 0, p.Name));
        return new FunctionTable(functions);
    }

    public static FunctionTable Load(string path)
    {
        var functions = new List<FunctionSymbol>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length == 3)
                functions.Add(new FunctionSymbol(ParseHex(parts[0]), ParseHex(parts[1]), parts[2]));
        }
        return new FunctionTable(functions);
    }

    public void Save(string path)
    {
        var temporary = path + ".tmp";
        using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
        {
            foreach (var function in Functions)
                writer.Write($"{function.Address:x}\t{function.Size:x}\t{function.Name}\n");
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static ulong ParseHex(string value) => ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<address>[0-9a-fA-F]+) (?:(?<size>[0-9a-fA-F]+) )?(?<type>[A-Za-z]) (?<name>\S+)\s*$")]
    private static partial Regex ElfNmLineRegex();

    [GeneratedRegex(@"^(?<address>[0-9a-fA-F]+) \(__TEXT,[^)]+\) .* (?<name>\S+)\s*$")]
    private static partial Regex MachONmLineRegex();

    [GeneratedRegex(@"^\s*SECTION HEADER #(\d+)")]
    private static partial Regex SectionHeaderRegex();

    [GeneratedRegex(@"^\s*([0-9A-Fa-f]+) virtual address\s*$")]
    private static partial Regex SectionAddressRegex();

    [GeneratedRegex(@"S_PUB32 \[size = \d+\] `([^`]+)`")]
    private static partial Regex PublicNameRegex();

    [GeneratedRegex(@"addr = (\d+):(\d+)")]
    private static partial Regex PublicAddressRegex();
}
