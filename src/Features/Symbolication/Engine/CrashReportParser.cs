using System.Globalization;
using System.Text.RegularExpressions;
using Aptabase.Features.Symbolication.Store;

namespace Aptabase.Features.Symbolication.Engine;

// Parses the stack traces the Godot SDK sends: native crash reports from Godot's crash handler and NativeAOT exception
// traces. Every line is kept, lines that aren't frames as plain text, so the result can be shown in the same layout.
public static partial class CrashReportParser
{
    public const string BuildHeaderPrefix = "Build: ";

    public static CrashReport Parse(string stackTrace)
    {
        var lines = new List<ReportLine>();
        var modules = new Dictionary<string, ReportModule>();
        string? buildId = null;
        string? rid = null;

        var rawLines = stackTrace.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < rawLines.Length; i++)
        {
            var text = rawLines[i];

            // The trailing newline of the last line isn't a line of its own
            if (i == rawLines.Length - 1 && text.Length == 0)
                break;

            if (i == 0 && BuildHeaderRegex().Match(text) is { Success: true } header)
            {
                buildId = header.Groups["build"].Success ? header.Groups["build"].Value : null;
                rid = header.Groups["rid"].Value;
                lines.Add(new ReportLine(text));
                continue;
            }

            if (NativeFrameRegex().Match(text) is { Success: true } native)
            {
                var reported = native.Groups["reported"].Success ? native.Groups["reported"].Value.Trim() : null;
                lines.Add(new ReportLine(text, Native: new NativeFrame(
                    int.Parse(native.Groups["index"].Value, CultureInfo.InvariantCulture),
                    native.Groups["module"].Value,
                    ParseHex(native.Groups["offset"].Value),
                    string.IsNullOrEmpty(reported) ? null : reported)));
                continue;
            }

            if (ModuleRegex().Match(text) is { Success: true } module)
            {
                var id = module.Groups["buildId"].Success ? module.Groups["buildId"].Value
                    : module.Groups["pdbId"].Success ? module.Groups["pdbId"].Value
                    : module.Groups["uuid"].Success ? module.Groups["uuid"].Value
                    : null;
                var name = module.Groups["name"].Value.Trim();
                modules.TryAdd(name, new ReportModule(name, module.Groups["file"].Value, id is null ? null : SymbolFileInspector.NormalizeDebugId(id)));
                lines.Add(new ReportLine(text));
                continue;
            }

            if (ManagedAddressFrameRegex().Match(text) is { Success: true } address)
            {
                lines.Add(new ReportLine(text, Managed: new ManagedFrame(ParseHex(address.Groups["offset"].Value), null, address.Groups["module"].Value)));
                continue;
            }

            if (ManagedMethodFrameRegex().Match(text) is { Success: true } method)
            {
                lines.Add(new ReportLine(text, Managed: new ManagedFrame(ParseHex(method.Groups["offset"].Value), method.Groups["name"].Value, null)));
                continue;
            }

            lines.Add(new ReportLine(text));
        }

        return new CrashReport { BuildId = buildId, Rid = rid, Lines = lines, Modules = modules };
    }

    // Only stack traces written by the SDK start with this header, others have nothing to symbolicate
    public static bool IsEligible(string? stackTrace) => stackTrace?.StartsWith(BuildHeaderPrefix, StringComparison.Ordinal) ?? false;

    private static ulong ParseHex(string value) => ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // "Build: 0-5-0-1793183957-Full_Release (linux-x64)", or "Build: (linux-x64)" when the game set no build ID
    [GeneratedRegex(@"^Build: (?:(?<build>\S+) )?\((?<rid>[^()\s]+)\)\s*$")]
    private static partial Regex BuildHeaderRegex();

    // "[3] 7f2465a984d0 (SampleGame.so+4984d0) - SampleGame.so(+0x4984d0) [0x7f2465a984d0]". The absolute address
    // is matched loosely, the PII sanitizer may have replaced it (it can look like a card number).
    [GeneratedRegex(@"^\s*\[(?<index>\d+)\] (?<address>\S+) \((?<module>[^()\s]+?)\+(?<offset>[0-9a-fA-F]+)\)(?: - (?<reported>.*))?\s*$")]
    private static partial Regex NativeFrameRegex();

    // "libc.so.6 (libc.so.6) - base: 7f24f8000000, build ID: 42c8...", or "PDB: <name>.pdb, PDB ID: <id>" (Windows), or "UUID: <uuid>" (macOS)
    [GeneratedRegex(@"^(?<name>[^()]+?) \((?<file>[^)]*)\) - base: [0-9a-fA-F]+(?:, (?:build ID: (?<buildId>[0-9a-fA-F]+)|PDB: [^,]*, PDB ID: (?<pdbId>[0-9a-fA-F]+)|UUID: (?<uuid>[0-9a-fA-F-]+)))?")]
    private static partial Regex ModuleRegex();

    // "   at SampleGame!<BaseAddress>+0x6fd9ef"
    [GeneratedRegex(@"^\s*at (?<module>[^\s!]+)!<BaseAddress>\+0x(?<offset>[0-9a-fA-F]+)\s*$")]
    private static partial Regex ManagedAddressFrameRegex();

    // "   at BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0() + 0x30"
    [GeneratedRegex(@"^\s*at (?<name>[^\s(]+)(?:\(.*\))? \+ 0x(?<offset>[0-9a-fA-F]+)\s*$")]
    private static partial Regex ManagedMethodFrameRegex();
}
