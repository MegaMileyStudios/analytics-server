using System.Globalization;
using System.Text.Json;

namespace Aptabase.Features.Symbolication.Engine;

public record SymbolizedLocation(string FunctionName, string? FileName, int Line, int Column);

// The locations of one address, innermost inlined function first and the real function last.
// Error is set when llvm-symbolizer couldn't read the symbol file.
public record SymbolizedAddress(IReadOnlyList<SymbolizedLocation> Locations, string? Error = null);

public class LlvmSymbolizer
{
    private readonly LlvmTools _tools;

    public LlvmSymbolizer(LlvmTools tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    // One process per symbol file with all of its addresses: loading a 500 MB engine file is the slow part
    public async Task<IReadOnlyList<SymbolizedAddress>> SymbolizeAsync(string objectPath, string? arch, IReadOnlyList<ulong> addresses, CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
            return [];

        var arguments = new List<string> { $"--obj={objectPath}", "--output-style=JSON", "--inlining" };
        if (arch is not null)
            arguments.Add($"--default-arch={arch}");
        arguments.AddRange(addresses.Select(a => $"0x{a:x}"));

        var output = await _tools.RunAsync(LlvmTools.Symbolizer, arguments, cancellationToken);
        var results = ParseJson(output);
        if (results.Count != addresses.Count)
            throw new InvalidOperationException($"llvm-symbolizer returned {results.Count} results for {addresses.Count} addresses");

        return results;
    }

    public static IReadOnlyList<SymbolizedAddress> ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = new List<SymbolizedAddress>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("Error", out var error))
            {
                var message = error.TryGetProperty("Message", out var m) ? m.GetString() : error.ToString();
                results.Add(new SymbolizedAddress([], message ?? "unknown error"));
                continue;
            }

            var locations = new List<SymbolizedLocation>();
            if (entry.TryGetProperty("Symbol", out var symbols))
            {
                foreach (var symbol in symbols.EnumerateArray())
                {
                    var function = GetString(symbol, "FunctionName");
                    if (string.IsNullOrEmpty(function) || function == "??")
                        continue;

                    var file = GetString(symbol, "FileName");
                    locations.Add(new SymbolizedLocation(
                        function,
                        string.IsNullOrEmpty(file) || file == "??" ? null : file,
                        GetInt(symbol, "Line"),
                        GetInt(symbol, "Column")));
                }
            }
            results.Add(new SymbolizedAddress(locations));
        }
        return results;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetInt32();
        return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}
