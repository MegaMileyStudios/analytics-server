using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aptabase.Features.Symbolication.Engine;

public static class FrameStatus
{
    // Resolved with uploaded symbols
    public const string Resolved = "resolved";
    // A system library frame the crashing process named itself
    public const string Named = "named";
    public const string Unresolved = "unresolved";
}

public static class ModuleRole
{
    public const string Engine = "engine";
    public const string Game = "game";
    public const string System = "system";
}

public static class SymbolicationStatus
{
    // The report has no frames to symbolicate
    public const string None = "none";
    public const string Complete = "complete";
    public const string Partial = "partial";
    public const string Unresolved = "unresolved";
}

public class ResultSymbol
{
    public string Function { get; set; } = "";
    public string RawFunction { get; set; } = "";
    public string? Location { get; set; }
    public bool NoSourceInfo { get; set; }
    public bool Inlined { get; set; }
}

public class ResultFrame
{
    public string Kind { get; set; } = "";
    public int? Index { get; set; }
    public string Module { get; set; } = "";
    public string ModuleRole { get; set; } = Engine.ModuleRole.System;
    public string? Offset { get; set; }
    public string Status { get; set; } = FrameStatus.Unresolved;
    public bool CrashHandler { get; set; }
    public string? Reason { get; set; }
    public string? ReportedName { get; set; }
    public List<ResultSymbol> Symbols { get; set; } = [];
}

public class ResultLine
{
    public string Text { get; set; } = "";
    public ResultFrame? Frame { get; set; }
}

public class MissingSymbols
{
    public string Module { get; set; } = "";
    public string Key { get; set; } = "";
}

// The symbolicated version of a report, stored as JSON next to the original
public class SymbolicationResult
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Version { get; set; } = CurrentVersion;
    public string? BuildId { get; set; }
    public string? Rid { get; set; }
    public int ResolvedFrames { get; set; }
    public int TotalFrames { get; set; }

    // A debug ID, or build:<build id>/<rid> for the game's C# frames; uploading one of them re-symbolicates the report
    public List<string> MissingKeys { get; set; } = [];
    public List<MissingSymbols> Missing { get; set; } = [];
    public List<ResultLine> Lines { get; set; } = [];

    public string Status => TotalFrames == 0 ? SymbolicationStatus.None
        : ResolvedFrames == TotalFrames ? SymbolicationStatus.Complete
        : ResolvedFrames == 0 ? SymbolicationStatus.Unresolved
        : SymbolicationStatus.Partial;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SymbolicationResult? FromJson(string json) => JsonSerializer.Deserialize<SymbolicationResult>(json, JsonOptions);

    // The report as text with every frame replaced by its symbolicated form, used for copying
    public string ToText()
    {
        var text = new StringBuilder();
        foreach (var line in Lines)
        {
            if (line.Frame is null)
            {
                text.Append(line.Text).Append('\n');
                continue;
            }

            var frame = line.Frame;
            var prefix = frame.Index is { } index ? $"[{index}] " : "   at ";
            if (frame.Symbols.Count == 0)
            {
                var name = frame.Status == FrameStatus.Named ? frame.ReportedName : $"{frame.Module}+{frame.Offset}";
                var note = frame.Status == FrameStatus.Named ? "named in the report" : frame.Reason;
                text.Append(prefix).Append(name).Append("  (").Append(note).Append(")  [").Append(frame.Module).Append("]\n");
                continue;
            }

            for (var i = 0; i < frame.Symbols.Count; i++)
            {
                var symbol = frame.Symbols[i];
                text.Append(i == 0 ? prefix : new string(' ', prefix.Length)).Append(symbol.Function);
                if (symbol.Location is not null)
                    text.Append("  ").Append(symbol.Location);
                else if (symbol.NoSourceInfo)
                    text.Append("  (no source info)");
                text.Append("  [").Append(frame.Module).Append(']');
                if (symbol.Inlined)
                    text.Append("  (inlined)");
                text.Append('\n');
            }
        }
        return text.ToString();
    }
}
