using System.Text.RegularExpressions;

namespace Aptabase.Features.Symbolication.Engine;

// Makes symbolizer output readable: short function names, repo-relative paths, and no lines that can't be trusted
public static partial class FrameFormatter
{
    // Including the architecture suffixes of Godot's Linux exports (BallsyDrop.x86_64)
    private static readonly string[] ModuleExtensions = [".dbg", ".debug", ".debugsymbols", ".pdb", ".so", ".dll", ".dylib", ".exe", ".x86_64", ".x86_32", ".arm64", ".arm32"];

    // A NativeAOT-compiled method: <Assembly>_<Namespace with _>_<Type>__<Method>, no C++ scope or parameter list
    public static bool IsManaged(string functionName)
    {
        var name = ManagedNameMatcher.RemoveGenericArguments(functionName);
        if (name.Contains("::") || name.Contains('(') || name.Contains(' '))
            return false;

        var separator = name.IndexOf("__", StringComparison.Ordinal);
        return separator > 0 && !name.StartsWith('_') && separator < name.Length - 2;
    }

    // NativeAOT names become <Type>.<Method>, the type being the part before the first "__", after its last "_".
    // MSVC names lose the class/struct/enum keywords ("(class String const &)" -> "(String const &)").
    public static string PrettyFunction(string functionName)
    {
        if (IsManaged(functionName))
        {
            var name = ManagedNameMatcher.RemoveGenericArguments(functionName);
            var separator = name.IndexOf("__", StringComparison.Ordinal);
            var segments = name[..separator].Split('_');

            // A generic type's arity stays with its name: List_1
            var type = segments.Length > 1 && segments[^1].All(char.IsAsciiDigit) ? $"{segments[^2]}_{segments[^1]}" : segments[^1];
            return $"{type}.{name[(separator + 2)..]}";
        }

        return MsvcKeywordRegex().Replace(functionName, "");
    }

    // C# lines can only be trusted for the game's own code and for .NET's libraries, whose debug info points at
    // Microsoft's sources (/_/src/...). GodotSharp has no line info: Windows and macOS say so, but on Linux the
    // debug info points at the previous entry of the line table, an unrelated game file.
    public static bool HasTrustedSource(string functionName, string? fileName, string? gameAssembly)
    {
        if (!IsManaged(functionName))
            return true;

        if (gameAssembly is not null && functionName.StartsWith(gameAssembly + "_", StringComparison.OrdinalIgnoreCase))
            return true;

        return fileName is not null && fileName.StartsWith("/_/", StringComparison.Ordinal);
    }

    // Repo-relative paths on every platform: the CI checkout prefix goes, Windows paths get forward slashes,
    // and source-generated files are shown by name only
    public static string PrettyPath(string path)
    {
        var result = CiPrefixRegex().Replace(path, "").Replace('\\', '/');
        if (result.StartsWith("./"))
            result = result[2..];

        if (result.Contains(".godot/mono/temp/obj/") || result.EndsWith(".generated.cs") || result.EndsWith(".g.cs"))
            result = result[(result.LastIndexOf('/') + 1)..];

        return result;
    }

    public static string? Location(SymbolizedLocation location)
    {
        if (location.FileName is null)
            return null;

        var path = PrettyPath(location.FileName);
        return location.Line > 0 ? $"{path}:{location.Line}" : path;
    }

    // The name the crashing process found itself. Linux prints "module(symbol+offset) [address]", usually without a symbol,
    // Windows "<couldn't map PC to fn name>", macOS the symbol ("_sigtramp + 56"), or "??" when it has none.
    public static string? ReportedName(string? reported)
    {
        if (string.IsNullOrWhiteSpace(reported) || reported == "??" || reported.StartsWith('<'))
            return null;

        if (LinuxReportedRegex().Match(reported) is { Success: true } linux)
        {
            var symbol = linux.Groups["symbol"].Value;
            return symbol.Length > 0 && !symbol.StartsWith('+') ? symbol : null;
        }

        return reported;
    }

    // "BallsyDrop.so.dbg" -> "BallsyDrop", the NativeAOT assembly prefix of the game's own methods
    public static string AssemblyName(string moduleName)
    {
        var name = moduleName;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var extension in ModuleExtensions)
            {
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && name.Length > extension.Length)
                {
                    name = name[..^extension.Length];
                    changed = true;
                }
            }
        }
        return name;
    }

    [GeneratedRegex(@"\b(?:class|struct|enum) ")]
    private static partial Regex MsvcKeywordRegex();

    // Linux CI: /workspace/<org>/<repo>/, Windows and macOS CI (act): everything up to .../hostexecutor/
    [GeneratedRegex(@"^(?:/workspace/[^/]+/[^/]+/|.*[/\\]hostexecutor[/\\])")]
    private static partial Regex CiPrefixRegex();

    [GeneratedRegex(@"^\S+\((?<symbol>[^)]*)\) \[0x[0-9a-fA-F]+\]$")]
    private static partial Regex LinuxReportedRegex();
}
