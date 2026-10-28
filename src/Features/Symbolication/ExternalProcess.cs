using System.Diagnostics;
using System.Text;

namespace Aptabase.Features.Symbolication;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public static class ExternalProcess
{
    // Runs a tool without a shell, so arguments (file paths, addresses) are never interpreted
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // Both streams are read while the process runs, a full pipe buffer would otherwise block it
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException($"{fileName} didn't finish within {timeout.TotalSeconds:0} seconds");
        }
    }
}
