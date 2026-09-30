using Aptabase.Features.Ingestion.Buffer;

namespace Aptabase.Features.ViewTracking.Buffer;

public readonly struct ViewRow
{
    public readonly string AppId;
    public readonly DateTime Timestamp;
    public readonly string SessionId;
    public readonly string? PersistentUserId;
    public readonly short? TimezoneOffset;
    public readonly string ViewType;
    public readonly string ViewName;
    public readonly string OSName;
    public readonly string OSVersion;
    public readonly string DeviceModel;
    public readonly string Locale;
    public readonly string AppVersion;
    public readonly string AppBuildNumber;
    public readonly string EngineName;
    public readonly string EngineVersion;
    public readonly string SdkVersion;
    public readonly string CountryCode;
    public readonly string RegionName;
    public readonly DateTime TTL;

    public ViewRow(ref TrackingView v)
    {
        // Kept as long as the regular events of the same build
        var ttl = v.IsDebug ? EventRow.DebugTTL : EventRow.ReleaseTTL;

        AppId = v.IsDebug ? $"{v.AppId}_DEBUG" : v.AppId;
        Timestamp = v.Timestamp;
        SessionId = v.SessionId;
        PersistentUserId = v.PersistentUserId;
        TimezoneOffset = v.TimezoneOffset;
        ViewType = v.ViewType;
        ViewName = v.ViewName;
        OSName = v.OSName ?? "";
        OSVersion = v.OSVersion ?? "";
        DeviceModel = v.DeviceModel ?? "";
        Locale = v.Locale ?? "";
        AppVersion = v.AppVersion ?? "";
        AppBuildNumber = v.AppBuildNumber ?? "";
        EngineName = v.EngineName ?? "";
        EngineVersion = v.EngineVersion ?? "";
        SdkVersion = v.SdkVersion ?? "";
        CountryCode = v.CountryCode ?? "";
        RegionName = v.RegionName ?? "";
        TTL = v.Timestamp.Add(ttl);
    }
}
