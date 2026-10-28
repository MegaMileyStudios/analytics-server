using System.Text.RegularExpressions;

namespace Aptabase.Features.Symbolication;

public enum ReportPlatform
{
    Unknown,
    Linux,
    Windows,
    MacOS,
}

// .NET runtime identifiers (linux-x64, win-x64, osx-arm64, ...) as they appear in the "Build:" line and in symbol archive paths
public static partial class RuntimeIds
{
    public static bool IsRid(string value) => RidRegex().IsMatch(value);

    public static ReportPlatform Platform(string? rid)
    {
        if (rid is null)
            return ReportPlatform.Unknown;
        if (rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase))
            return ReportPlatform.Linux;
        if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
            return ReportPlatform.Windows;
        if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase) || rid.StartsWith("macos", StringComparison.OrdinalIgnoreCase))
            return ReportPlatform.MacOS;
        return ReportPlatform.Unknown;
    }

    // The architecture name the LLVM tools use for a Mach-O slice, e.g. osx-x64 -> x86_64
    public static string? MachOArch(string? rid)
    {
        if (rid is null)
            return null;
        if (rid.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase))
            return "arm64";
        if (rid.EndsWith("-x64", StringComparison.OrdinalIgnoreCase))
            return "x86_64";
        return null;
    }

    // The RID a symbol file is for, when the archive doesn't say so in a folder name
    public static string? FromArch(string format, string? arch)
    {
        var os = format switch
        {
            Store.SymbolFormats.Elf => "linux",
            Store.SymbolFormats.MachO => "osx",
            _ => null,
        };
        var cpu = arch switch
        {
            "x86_64" => "x64",
            "aarch64" or "arm64" => "arm64",
            _ => null,
        };
        return os is null || cpu is null ? null : $"{os}-{cpu}";
    }

    [GeneratedRegex(@"^(linux|linux-musl|win|osx)-(x64|x86|arm64|arm)$")]
    private static partial Regex RidRegex();
}
