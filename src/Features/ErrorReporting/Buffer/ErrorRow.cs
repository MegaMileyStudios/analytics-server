using System.Globalization;
using System.Text.Json;

namespace Aptabase.Features.ErrorReporting.Buffer;

public readonly struct ErrorRow
{
    // Errors are kept for 180 days (6 months)
    public static readonly TimeSpan ErrorTTL = TimeSpan.FromDays(180);

    public readonly string ErrorId;
    public readonly string AppId;
    public readonly DateTime Timestamp;
    public readonly string ErrorMessage;
    public readonly string ErrorType;
    public readonly string StackTrace;
    public readonly string Platform;
    public readonly string OsName;
    public readonly string OsVersion;
    public readonly string AppVersion;
    public readonly string SdkVersion;
    public readonly string SessionId;
    public readonly string? PersistentUserId;
    public readonly short? TimezoneOffset;
    public readonly string Severity;
    public readonly string Kind;
    public readonly DateTime TTL;

    public ErrorRow(ref TrackingError e)
    {
        ErrorId = e.ErrorId;
        AppId = e.IsDebug ? $"{e.AppId}_DEBUG" : e.AppId;
        Timestamp = e.Timestamp;
        ErrorMessage = e.ErrorMessage;
        ErrorType = e.ErrorType;
        StackTrace = e.StackTrace ?? "";
        Platform = e.Platform ?? "";
        OsName = e.OsName ?? "";
        OsVersion = e.OsVersion ?? "";
        AppVersion = e.AppVersion ?? "";
        SdkVersion = e.SdkVersion ?? "";
        SessionId = e.SessionId ?? "";
        PersistentUserId = e.PersistentUserId;
        TimezoneOffset = e.TimezoneOffset;
        Severity = e.Severity ?? "";
        Kind = e.Kind ?? "";
        TTL = e.Timestamp.Add(ErrorTTL);
    }

    public void WriteJson(StringWriter writer)
    {
        writer.Write("{");
        WriteProperty(writer, "errorId", ErrorId);
        WriteProperty(writer, "appId", AppId);
        WriteProperty(writer, "timestamp", Timestamp.ToString("o"));
        WriteProperty(writer, "errorMessage", ErrorMessage);
        WriteProperty(writer, "errorType", ErrorType);
        WriteProperty(writer, "stackTrace", StackTrace);
        WriteProperty(writer, "platform", Platform);
        WriteProperty(writer, "osName", OsName);
        WriteProperty(writer, "osVersion", OsVersion);
        WriteProperty(writer, "appVersion", AppVersion);
        WriteProperty(writer, "sdkVersion", SdkVersion);
        WriteProperty(writer, "sessionId", SessionId);
        WriteNullableProperty(writer, "persistentUserId", PersistentUserId);
        WriteProperty(writer, "severity", Severity);
        WriteProperty(writer, "kind", Kind);
        WriteNullableNumber(writer, "timezoneOffset", TimezoneOffset);
        WriteProperty(writer, "ttl", TTL.ToString("o"), true);
        writer.Write("}");
    }

    private static void WriteProperty(StringWriter writer, string name, string value, bool isLast = false)
    {
        writer.Write($"\"{name}\": \"{JsonEncodedText.Encode(value)}\" {(isLast ? "" : ",")}");
    }

    private static void WriteNullableProperty(StringWriter writer, string name, string? value, bool isLast = false)
    {
        if (value is null)
            writer.Write($"\"{name}\": null {(isLast ? "" : ",")}");
        else
            WriteProperty(writer, name, value, isLast);
    }

    private static void WriteNullableNumber(StringWriter writer, string name, short? value, bool isLast = false)
    {
        writer.Write($"\"{name}\": {(value is null ? "null" : value.Value.ToString(CultureInfo.InvariantCulture))} {(isLast ? "" : ",")}");
    }
}
