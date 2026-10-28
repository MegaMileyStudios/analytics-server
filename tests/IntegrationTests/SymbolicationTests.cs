using Xunit;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Aptabase.Features.ErrorReporting.Buffer;
using Aptabase.Features.Symbolication;
using Aptabase.Features.Symbolication.Store;
using Aptabase.IntegrationTests.Clients;

namespace Aptabase.IntegrationTests;

// Uses the symbol fixtures from tests/Fixtures/Symbols (see build-fixtures.sh): a tiny library per platform whose
// functions are named like NativeAOT methods, Game_Game_Main__Crash called by Game_Game_Main__Run.
// The symbol store is shared by the whole server, so every test removes the keys and symbols it added afterwards.
[Collection("Integration Tests")]
public class SymbolicationTests : IAsyncLifetime
{
    private static readonly Random _random = new();
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Symbols");
    private static readonly JsonObject Fixtures = JsonNode.Parse(File.ReadAllText(Path.Combine(FixturesDirectory, "fixtures.json")))!.AsObject();

    private readonly IntegrationTestsFixture _fixture;
    private readonly ErrorBackgroundWritter _errorWritter;
    private readonly SymbolicationWorker _worker;
    private readonly List<string> _createdKeys = [];

    public SymbolicationTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
        _errorWritter = _fixture.GetHostedService<ErrorBackgroundWritter>();
        _worker = _fixture.GetHostedService<SymbolicationWorker>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var id in _createdKeys)
            await _fixture.UserA.DeleteSymbolUploadKey(id);
        await RemoveFixtureSymbols();
    }

    private static readonly string[] Rids = ["linux-x64", "win-x64", "osx-arm64"];

    public static TheoryData<string> Platforms => new(Rids);

    [Fact]
    public async Task Upload_Needs_A_Valid_Key()
    {
        (await Upload("linux-x64", key: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Upload("linux-x64", key: "SYM-not-a-key")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (id, secret) = await CreateKey();
        (await _fixture.UserA.DeleteSymbolUploadKey(id)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Upload("linux-x64", key: secret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Keys_Are_Listed_Without_Their_Secret()
    {
        var (id, secret) = await CreateKey();

        var keys = await ReadJson(await _fixture.UserA.ListSymbolUploadKeys());
        var key = keys.AsArray().Single(k => k!["id"]!.GetValue<string>() == id)!;
        key["keyPrefix"]!.GetValue<string>().Should().Be(secret[..12]);
        keys.ToJsonString().Should().NotContain(secret);
    }

    [Fact]
    public async Task Users_Without_Apps_Cannot_Manage_Symbols()
    {
        // Its own IP, so this sign-up doesn't count towards the SignUp rate limit of the other tests
        var http = _fixture.CreateClient();
        http.DefaultRequestHeaders.Add("X-Real-Ip", $"10.{_random.Next(0, 255)}.{_random.Next(0, 255)}.{_random.Next(1, 255)}");
        var user = new AccountClient(http);
        await user.CreateAccount("Hodor", $"hodor.{Guid.NewGuid()}@got.com");

        (await user.ListSymbolUploadKeys()).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.ListSymbolUploads()).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.CreateSymbolUploadKey("nope")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Upload_Keeps_Only_The_Symbol_Files(string rid)
    {
        await RemoveFixtureSymbols();
        var (_, key) = await CreateKey();

        var response = await Upload(rid, key, buildId: NewBuildId());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await ReadJson(response);

        var file = summary["files"]!.AsArray().Should().ContainSingle().Subject!;
        file["debugId"]!.GetValue<string>().Should().Be(DebugId(rid));
        file["rid"]!.GetValue<string>().Should().Be(rid);
        var (kept, skipped) = rid switch
        {
            "linux-x64" => ("linux-x64/Game.so.dbg", "linux-x64/Game.so"),
            "win-x64" => ("win-x64/Game.pdb", "win-x64/Game.dll"),
            _ => ("osx-arm64/Game.dylib.dSYM/Contents/Resources/DWARF/Game.dylib", "osx-arm64/Game.dylib"),
        };
        file["path"]!.GetValue<string>().Should().Be(kept);
        summary["skipped"]!.AsArray().Select(s => s!["path"]!.GetValue<string>()).Should().Contain(skipped);

        var uploadId = summary["uploadId"]!.GetValue<string>();
        var folder = Path.Combine(SymbolsRoot, uploadId);
        File.Exists(Path.Combine(folder, kept)).Should().BeTrue();
        File.Exists(Path.Combine(folder, skipped)).Should().BeFalse();
        if (rid == "win-x64")
            File.Exists(Path.Combine(folder, kept + PdbStub.FileSuffix)).Should().BeTrue();

        var uploads = await ReadJson(await _fixture.UserA.ListSymbolUploads());
        uploads["uploads"]!.AsArray().Should().Contain(u => u!["id"]!.GetValue<string>() == uploadId);
    }

    [Fact]
    public async Task Uploading_The_Same_Symbols_Again_Stores_Nothing_New()
    {
        await RemoveFixtureSymbols();
        var (_, key) = await CreateKey();
        (await Upload("linux-x64", key)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Upload("linux-x64", key);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await ReadJson(response);
        summary["uploadId"].Should().BeNull();
        summary["alreadyStored"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task An_Archive_Without_Symbols_Is_Rejected()
    {
        var (_, key) = await CreateKey();

        var response = await UploadBytes([0x1f, 0x8b, 0x08, 0x00, 0x00], key, null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Native_Crash_Is_Symbolicated(string rid)
    {
        await RemoveFixtureSymbols();
        var (_, key) = await CreateKey();
        var buildId = NewBuildId();
        (await Upload(rid, key, buildId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var errorId = await TrackError(app, NativeCrash(rid, buildId), "crash");
        await _worker.ProcessPending(CancellationToken.None);

        var result = await GetResult(app.Id, errorId);
        var frames = Frames(result);
        var (crashIndex, runIndex) = rid == "win-x64" ? (0, 1) : (2, 3);

        AssertFunction(frames[crashIndex], "Main.Crash", "Game_Game_Main__Crash(int value)", "// crash-line");
        AssertFunction(frames[runIndex], "Main.Run", "Game_Game_Main__Run(int value)", "// call-line");
        frames[crashIndex]["moduleRole"]!.GetValue<string>().Should().Be("game");

        var system = frames.Values.Single(f => f["moduleRole"]!.GetValue<string>() == "system" && !f["crashHandler"]!.GetValue<bool>());
        if (rid == "win-x64")
        {
            system["status"]!.GetValue<string>().Should().Be("unresolved");
            system["reason"]!.GetValue<string>().Should().StartWith("System library");
            result["result"]!["missing"]!.AsArray().Should().ContainSingle();
        }
        else
        {
            // libc's __libc_start_main and macOS' _sigtramp are named in the report
            system["status"]!.GetValue<string>().Should().Be("named");
        }

        result["text"]!.GetValue<string>().Should().Contain("Main.Crash  fixture.c:");
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Managed_Frames_With_Method_Names_Are_Symbolicated(string rid)
    {
        await RemoveFixtureSymbols();
        var (_, key) = await CreateKey();
        var buildId = NewBuildId();
        (await Upload(rid, key, buildId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var stackTrace = $"""
            Build: {buildId} ({rid})
            System.InvalidOperationException: boom
               at Game.Main.Crash(Int32) + 0x4
               at Game.Main.Run(Int32) + 0x8
            """;
        var errorId = await TrackError(app, stackTrace, "unhandled");
        await _worker.ProcessPending(CancellationToken.None);

        var lines = (await GetResult(app.Id, errorId))["result"]!["lines"]!.AsArray();
        var frames = lines.Select(l => l!["frame"]).OfType<JsonNode>().ToArray();

        frames.Should().HaveCount(2);
        AssertFunction(frames[0], "Main.Crash", "Game_Game_Main__Crash(int value)", "// crash-line");
        AssertFunction(frames[1], "Main.Run", "Game_Game_Main__Run(int value)", "// call-line");
    }

    [Fact]
    public async Task Address_Only_Managed_Frames_Use_The_Build_Id()
    {
        await RemoveFixtureSymbols();
        var (_, key) = await CreateKey();
        var buildId = NewBuildId();
        (await Upload("linux-x64", key, buildId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var stackTrace = $"""
            Build: {buildId} (linux-x64)
            System.InvalidOperationException: boom
               at Game!<BaseAddress>+0x{Function("linux-x64", "Game_Game_Main__Run") + 8:x}
               at Game!<BaseAddress>+0x10
            """;
        var errorId = await TrackError(app, stackTrace, "unhandled");
        await _worker.ProcessPending(CancellationToken.None);

        var frames = (await GetResult(app.Id, errorId))["result"]!["lines"]!.AsArray().Select(l => l!["frame"]).OfType<JsonNode>().ToArray();
        AssertFunction(frames[0], "Main.Run", "Game_Game_Main__Run(int value)", "// call-line");
        frames[1]["status"]!.GetValue<string>().Should().Be("unresolved");
        frames[1]["reason"]!.GetValue<string>().Should().StartWith("No function at this address");
    }

    [Fact]
    public async Task Report_Is_Symbolicated_Again_When_Its_Symbols_Arrive()
    {
        await RemoveFixtureSymbols();
        var buildId = NewBuildId();
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var errorId = await TrackError(app, NativeCrash("linux-x64", buildId), "crash");

        await _worker.ProcessPending(CancellationToken.None);
        var before = await GetResult(app.Id, errorId);
        before["result"]!["missingKeys"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Contain(DebugId("linux-x64"));
        Frames(before)[2]["reason"]!.GetValue<string>().Should().Be($"No symbols uploaded for Game.so (ID {DebugId("linux-x64")})");

        var (_, key) = await CreateKey();
        (await Upload("linux-x64", key, buildId)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _worker.ProcessPending(CancellationToken.None);

        var after = await GetResult(app.Id, errorId);
        AssertFunction(Frames(after)[2], "Main.Crash", "Game_Game_Main__Crash(int value)", "// crash-line");
    }

    [Fact]
    public async Task Symbolicate_Runs_On_Request_And_Checks_Access()
    {
        await RemoveFixtureSymbols();
        var (_, key) = await CreateKey();
        var buildId = NewBuildId();
        (await Upload("win-x64", key, buildId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var errorId = await TrackError(app, NativeCrash("win-x64", buildId), "crash");

        var response = await _fixture.UserA.Symbolicate(app.Id, errorId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertFunction(Frames((await ReadJson(response))["symbolication"]!)[0], "Main.Crash", "Game_Game_Main__Crash(int value)", "// crash-line");

        (await _fixture.UserB.GetSymbolication(app.Id, errorId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _fixture.UserB.Symbolicate(app.Id, errorId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reports_Without_The_Sdk_Header_Are_Not_Eligible()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var errorId = await TrackError(app, "at MyApp.MainPage.OnButtonClicked()", "handled");

        var response = await ReadJson(await _fixture.UserA.GetSymbolication(app.Id, errorId));

        response["eligible"]!.GetValue<bool>().Should().BeFalse();
        response["symbolication"].Should().BeNull();
    }

    [Fact]
    public async Task Deleting_An_Upload_Removes_Its_Files()
    {
        await RemoveFixtureSymbols();
        var (_, key) = await CreateKey();
        var summary = await ReadJson(await Upload("osx-arm64", key));
        var uploadId = summary["uploadId"]!.GetValue<string>();
        Directory.Exists(Path.Combine(SymbolsRoot, uploadId)).Should().BeTrue();

        (await _fixture.UserA.DeleteSymbolUpload(uploadId)).StatusCode.Should().Be(HttpStatusCode.OK);

        Directory.Exists(Path.Combine(SymbolsRoot, uploadId)).Should().BeFalse();
        var store = _fixture.Services.GetRequiredService<ISymbolStore>();
        (await store.FindByDebugIds([DebugId("osx-arm64")], CancellationToken.None)).Should().BeEmpty();
    }

    private static string SymbolsRoot => Path.Combine(Path.GetTempPath(), "aptabase-integration-tests-symbols");

    private static string NewBuildId() => $"test-{Guid.NewGuid():N}";

    private static string DebugId(string rid) => Fixtures[rid]!["debugId"]!.GetValue<string>().Replace("-", "").ToUpperInvariant();

    private static ulong Function(string rid, string name) =>
        ulong.Parse(Fixtures[rid]!["functions"]![name]!.GetValue<string>()[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // A crash in Game_Game_Main__Crash, called by Game_Game_Main__Run, with a system library frame, in each platform's crash handler format
    private static string NativeCrash(string rid, string buildId)
    {
        var crash = Function(rid, "Game_Game_Main__Crash");
        var run = Function(rid, "Game_Game_Main__Run") + 8;
        var id = DebugId(rid);
        return rid switch
        {
            "linux-x64" => $"""
                Build: {buildId} (linux-x64)
                handle_crash: Program crashed with signal 11
                [1] 7f0000045330 (libc.so.6+45330) - libc.so.6(+0x45330) [0x7f0000045330]
                [2] 7f1000001000 (Game.so+{crash:x}) - Game.so(+0x{crash:x}) [0x7f1000001000]
                [3] 7f1000002000 (Game.so+{run:x}) - Game.so(+0x{run:x}) [0x7f1000002000]
                [4] 7f0000029d8b (libc.so.6+29d8b) - libc.so.6(__libc_start_main+0x8b) [0x7f0000029d8b]
                Modules:
                libc.so.6 (libc.so.6) - base: 7f0000000000, build ID: 42c84c92e6f98126b3e2230ebfdead22c235b667
                main (Game.x86_64) - base: 5f0000000000, build ID: 3128a633b44803a29dbeb2facfa7b764ec4ff026
                Game.so (Game.so) - base: 7f1000000000, build ID: {id.ToLowerInvariant()}
                -- END OF C++ BACKTRACE --
                """,
            "win-x64" => $"""
                Build: {buildId} (win-x64)
                CrashHandlerException: Program crashed
                [0] 7ff600001000 (game.dll+{crash:x}) - <couldn't map PC to fn name>
                [1] 7ff600002000 (game.dll+{run:x}) - <couldn't map PC to fn name>
                [2] 7ff988d6e957 (kernel32.dll+2e957) - <couldn't map PC to fn name>
                Modules:
                main (Game.exe) - base: 7ff6b9280000, PDB: godot.windows.template_release.x86_64.mono.pdb, PDB ID: 61D3E1A84C9141A2A6365AFCA3C2BC751
                game.dll (Game.dll) - base: 7ff600000000, PDB: Game.pdb, PDB ID: {id}
                kernel32.dll (KERNEL32.DLL) - base: 7ff988d40000, PDB: kernel32.pdb, PDB ID: 4E79D7A58C705A753A97CAF347FAF0C01
                -- END OF C++ BACKTRACE --
                """,
            _ => $"""
                Build: {buildId} (osx-arm64)
                handle_crash: Program crashed with signal 11
                [0] 100986648 (Game.dylib+{crash:x}) - godotsharp_game_main_init + 10
                [1] 1878b1744 (libsystem_platform.dylib+3744) - _sigtramp + 56
                [2] 123c23bf8 (Game.dylib+{crash:x}) - godotsharp_game_main_init + 20
                [3] 123b2d43c (Game.dylib+{run:x}) - godotsharp_game_main_init + 30
                [4] 1874dc4e4 (dyld+204e4) - start + 6992
                Modules:
                libsystem_platform.dylib (libsystem_platform.dylib) - base: 1878ae000, UUID: EDB83A19-EC17-32DE-9350-6145970A85D6
                main (Game) - base: 100268000, UUID: EB16A950-A45C-3AFB-90B6-FF978C7AF619
                Game.dylib (Game.dylib) - base: 1239f4000, UUID: {id[..8]}-{id[8..12]}-{id[12..16]}-{id[16..20]}-{id[20..]}
                dyld (dyld) - base: 1874bc000, UUID: 74E52480-C2BD-3C8D-812D-95FE2B74A096
                -- END OF C++ BACKTRACE --
                """,
        };
    }

    // The function's outermost symbol, and a line of fixture.c between the function's declaration and the marker
    private static void AssertFunction(JsonNode frame, string function, string declaration, string marker)
    {
        frame["status"]!.GetValue<string>().Should().Be("resolved", frame.ToJsonString());
        var symbol = frame["symbols"]!.AsArray().Last()!;
        symbol["function"]!.GetValue<string>().Should().Be(function);

        var location = symbol["location"]!.GetValue<string>();
        location.Should().StartWith("fixture.c:", "the CI checkout prefix is stripped");
        var line = int.Parse(location["fixture.c:".Length..], CultureInfo.InvariantCulture);
        var source = File.ReadAllLines(Path.Combine(FixturesDirectory, "fixture.c"));
        line.Should().BeInRange(Array.FindIndex(source, l => l.Contains(declaration)) + 1, Array.FindIndex(source, l => l.Contains(marker)) + 1);
    }

    private static Dictionary<int, JsonNode> Frames(JsonNode response) =>
        response["result"]!["lines"]!.AsArray()
            .Select(l => l!["frame"])
            .OfType<JsonNode>()
            .Where(f => f["index"] is not null)
            .ToDictionary(f => f["index"]!.GetValue<int>());

    private async Task<JsonNode> GetResult(string appId, string errorId)
    {
        var response = await ReadJson(await _fixture.UserA.GetSymbolication(appId, errorId));
        response["eligible"]!.GetValue<bool>().Should().BeTrue();
        return response["symbolication"] ?? throw new Exception("The report wasn't symbolicated");
    }

    private async Task<string> TrackError(Aptabase.Features.Apps.Application app, string stackTrace, string kind)
    {
        var client = new ErrorsClient(_fixture.CreateClient(), app.AppKey);
        var code = await client.TrackError(new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            errorType = "SIGSEGV",
            errorMessage = "Program crashed",
            stackTrace,
            platform = "Godot",
            osName = "Linux",
            osVersion = "6.8",
            appVersion = "1.0.0",
            sdkVersion = "MegaMileyStudios.Analytics.Godot@0.0.4",
            sessionId = IngestionClient.NewSessionId(),
            severity = "fatal",
            kind,
            isDebug = false,
        });
        code.Should().Be(HttpStatusCode.Accepted);
        await _errorWritter.FlushErrors();

        var list = await (await _fixture.UserA.GetErrors(app.Id)).Content.ReadFromJsonAsync<ErrorsListResponse>();
        return list!.Errors.Single().ErrorId;
    }

    // The fixtures have fixed debug IDs, and every ID is stored once, so tests start without them
    private async Task RemoveFixtureSymbols()
    {
        var store = _fixture.Services.GetRequiredService<ISymbolStore>();
        var uploads = _fixture.Services.GetRequiredService<SymbolUploadService>();
        var files = await store.FindByDebugIds(Rids.Select(DebugId).ToArray(), CancellationToken.None);
        foreach (var uploadId in files.Select(f => f.UploadId).Distinct())
            await uploads.DeleteAsync(uploadId, CancellationToken.None);
    }

    // Only app owners manage the symbol store
    private async Task<(string Id, string Secret)> CreateKey()
    {
        await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var response = await _fixture.UserA.CreateSymbolUploadKey($"Test {Guid.NewGuid():N}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJson(response);
        var id = json["key"]!["id"]!.GetValue<string>();
        _createdKeys.Add(id);
        return (id, json["secret"]!.GetValue<string>());
    }

    private async Task<HttpResponseMessage> Upload(string rid, string? key, string? buildId = null) =>
        await UploadBytes(await File.ReadAllBytesAsync(Path.Combine(FixturesDirectory, $"game-{rid}-symbols.tar.gz")), key, buildId, $"game-{rid}-symbols.tar.gz");

    private async Task<HttpResponseMessage> UploadBytes(byte[] archive, string? key, string? buildId, string name = "test.tar.gz")
    {
        var url = $"/api/v0/symbols?name={name}" + (buildId is null ? "" : $"&buildId={buildId}");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(archive) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        if (key is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("X-Real-Ip", $"10.{_random.Next(0, 255)}.{_random.Next(0, 255)}.{_random.Next(1, 255)}");
        return await _fixture.CreateClient().SendAsync(request);
    }

    private static async Task<JsonNode> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonNode.Parse(body)!;
    }
}
