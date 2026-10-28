using System.Text;
using System.Text.RegularExpressions;

namespace Aptabase.Features.Symbolication.Engine;

public record ManagedMatch(FunctionSymbol? Function, int CandidateCount, string? Reason);

// Finds the NativeAOT symbol of a method named in a stack trace ("BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0").
// NativeAOT names a method <assembly>_<type, every non-alphanumeric character as _>__<method>, e.g.
// BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0. The assembly isn't in the trace, so the
// symbol is matched on the rest, with any prefix that looks like an assembly name (no "__", e.g. S_P_CoreLib).
public static partial class ManagedNameMatcher
{
    public static ManagedMatch Find(FunctionTable table, string traceName, ulong offset)
    {
        var suffix = MangledSuffix(traceName);
        if (suffix is null)
            return new ManagedMatch(null, 0, $"Can't tell the type and method apart in {traceName}");

        // Overloads get a _<n> suffix
        var pattern = new Regex($"^[A-Za-z0-9]+(?:_[A-Za-z0-9]+)*_{Regex.Escape(suffix)}(?:_\\d+)?$", RegexOptions.CultureInvariant);
        var names = table.MatchNames;
        var candidates = Enumerable.Range(0, names.Count)
            .Where(i => names[i].Contains(suffix, StringComparison.Ordinal) && pattern.IsMatch(names[i]))
            .Select(i => table.Functions[i])
            .ToArray();
        if (candidates.Length == 0)
            return new ManagedMatch(null, 0, $"No symbol for {ShortName(traceName)} in the game's symbols");

        var containing = candidates.Where(f => f.Size == 0 || offset < f.Size).ToArray();
        if (containing.Length == 0)
            return new ManagedMatch(null, candidates.Length, $"Offset 0x{offset:x} lies outside {ShortName(traceName)}");

        return new ManagedMatch(containing[0], containing.Length, null);
    }

    // "A.B.C.<>c.<Trigger>b__0_0" -> "A_B_C___c___Trigger_b__0_0", or null when there's no method part
    public static string? MangledSuffix(string traceName)
    {
        var name = RemoveBracketGroups(traceName);
        var split = LastTopLevelDot(name);
        if (split <= 0 || split == name.Length - 1)
            return null;

        // Constructors: "Type..ctor" splits into "Type" and ".ctor"
        if (name[split - 1] == '.')
            split--;

        var type = name[..split];
        var method = name[(split + 1)..];
        return $"{Mangle(type)}__{Mangle(method)}";
    }

    // "BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0" -> "ErrorReportingTest.<>c.<Trigger>b__0_0":
    // the namespace is dropped, compiler-generated nested types stay with the type they're in
    public static string ShortName(string traceName)
    {
        var name = RemoveBracketGroups(traceName);
        var segments = SplitTopLevel(name);
        if (segments.Count <= 2)
            return name;

        var typeSegments = segments.Take(segments.Count - 1).ToList();
        var start = typeSegments.FindLastIndex(s => s.Length > 0 && !s.StartsWith('<'));
        if (start < 0)
            start = typeSegments.Count - 1;

        return string.Join('.', segments.Skip(start));
    }

    private static string Mangle(string value) => NonIdentifierRegex().Replace(value, "_");

    private static int LastTopLevelDot(string name)
    {
        var depth = 0;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            var c = name[i];
            if (c == '>')
                depth++;
            else if (c == '<')
                depth--;
            else if (c == '.' && depth == 0)
                return i;
        }
        return -1;
    }

    private static List<string> SplitTopLevel(string name)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '<')
                depth++;
            else if (c == '>')
                depth--;

            // ".ctor" keeps its dot
            if (c == '.' && depth == 0 && current.Length > 0)
            {
                segments.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        segments.Add(current.ToString());
        return segments;
    }

    // Generic arguments in a stack trace: "List`1[System.__Canon].Add" -> "List`1.Add"
    private static string RemoveBracketGroups(string value) => RemoveGroups(value, '[', ']');

    // Generic instantiations in a symbol: "S_P_CoreLib_System_Collections_Generic_List_1<System___Canon>__Add" -> "..._List_1__Add"
    public static string RemoveGenericArguments(string symbol) => symbol.Contains('<') ? RemoveGroups(symbol, '<', '>') : symbol;

    private static string RemoveGroups(string value, char open, char close)
    {
        var result = new StringBuilder(value.Length);
        var depth = 0;
        foreach (var c in value)
        {
            if (c == open)
                depth++;
            else if (c == close && depth > 0)
                depth--;
            else if (depth == 0)
                result.Append(c);
        }
        return result.ToString();
    }

    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex NonIdentifierRegex();
}
