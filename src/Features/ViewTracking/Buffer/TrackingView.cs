namespace Aptabase.Features.ViewTracking.Buffer;

public readonly struct TrackingView
{
    public required string AppId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string SessionId { get; init; }
    public string? PersistentUserId { get; init; }
    public short? TimezoneOffset { get; init; }
    public required string ViewType { get; init; }
    public required string ViewName { get; init; }
    public string OSName { get; init; }
    public string OSVersion { get; init; }
    public string DeviceModel { get; init; }
    public string Locale { get; init; }
    public string AppVersion { get; init; }
    public string AppBuildNumber { get; init; }
    public string EngineName { get; init; }
    public string EngineVersion { get; init; }
    public string SdkVersion { get; init; }
    public string CountryCode { get; init; }
    public string RegionName { get; init; }
    public bool IsDebug { get; init; }
}
