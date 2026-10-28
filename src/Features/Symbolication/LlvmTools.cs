namespace Aptabase.Features.Symbolication;

// The LLVM 18 tools (package llvm-18) and readelf (package binutils) that read and symbolicate debug symbols.
// All of them run on Linux, also for Windows (.pdb) and macOS (.dSYM) symbols.
public class LlvmTools
{
    public const string Symbolizer = "llvm-symbolizer-18";
    public const string PdbUtil = "llvm-pdbutil-18";
    public const string DwarfDump = "llvm-dwarfdump-18";
    public const string ObjDump = "llvm-objdump-18";
    public const string Nm = "llvm-nm-18";
    public const string ReadElf = "readelf";

    private static readonly string[] AllTools = [Symbolizer, PdbUtil, DwarfDump, ObjDump, Nm, ReadElf];
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger _logger;
    private readonly Lazy<string[]> _missingTools;

    public LlvmTools(ILogger<LlvmTools> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _missingTools = new Lazy<string[]>(FindMissingTools);
    }

    public bool IsAvailable => _missingTools.Value.Length == 0;

    public string MissingToolsMessage => $"Symbolication needs {string.Join(", ", _missingTools.Value)} on the server (packages llvm-18 and binutils)";

    public async Task<string> RunAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ExternalProcess.RunAsync(tool, arguments, DefaultTimeout, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{tool} exited with code {result.ExitCode}: {result.StandardError.Trim()}");

        return result.StandardOutput;
    }

    private string[] FindMissingTools()
    {
        var missing = AllTools.Where(tool => !CanRun(tool)).ToArray();
        if (missing.Length > 0)
            _logger.LogWarning("Crash symbolication is disabled, missing tools: {Tools}", string.Join(", ", missing));
        return missing;
    }

    private static bool CanRun(string tool)
    {
        try
        {
            var result = ExternalProcess.RunAsync(tool, ["--version"], TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            return result.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
