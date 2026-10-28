using Aptabase.Features.Symbolication;
using Xunit;

namespace Aptabase.UnitTests.Features.Symbolication;

// For tests that run the LLVM tools. CI installs them; locally the test is skipped when they're missing.
public sealed class RequiresLlvmFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            return ExternalProcess.RunAsync(LlvmTools.Symbolizer, ["--version"], TimeSpan.FromSeconds(10), CancellationToken.None)
                .GetAwaiter().GetResult().ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public RequiresLlvmFactAttribute()
    {
        if (!Available.Value)
            Skip = $"{LlvmTools.Symbolizer} is not installed (package llvm-18)";
    }
}
