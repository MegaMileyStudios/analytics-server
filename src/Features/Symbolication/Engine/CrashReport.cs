namespace Aptabase.Features.Symbolication.Engine;

// A line of the Modules list Godot's crash handler prints, DebugId is the GNU build ID, PDB ID or Mach-O UUID (normalized)
public record ReportModule(string Name, string File, string? DebugId);

// "[n] <address> (<module>+<offset>) - <name the crashing process found itself>"
public record NativeFrame(int Index, string Module, ulong Offset, string? Reported);

// A frame of a NativeAOT exception stack trace, either "at <Method>(...) + 0x<offset>" (StackTraceSupport on,
// the offset is inside the method) or "at <Module>!<BaseAddress>+0x<offset>" (StackTraceSupport off, the offset is inside the module)
public record ManagedFrame(ulong Offset, string? MethodName, string? Module);

public record ReportLine(string Text, NativeFrame? Native = null, ManagedFrame? Managed = null);

public class CrashReport
{
    // The game's build, from the "Build: <BuildId> (<RID>)" header the SDK writes at the top of every stack trace
    public string? BuildId { get; init; }
    public string? Rid { get; init; }
    public ReportPlatform Platform => RuntimeIds.Platform(Rid);

    public IReadOnlyList<ReportLine> Lines { get; init; } = [];
    public IReadOnlyDictionary<string, ReportModule> Modules { get; init; } = new Dictionary<string, ReportModule>();

    public bool HasFrames => Lines.Any(l => l.Native is not null || l.Managed is not null);
    public bool HasManagedFrames => Lines.Any(l => l.Managed is not null);
}
