using System.ComponentModel.DataAnnotations;
using Aptabase.Features.Ingestion;

namespace Aptabase.Features.ViewTracking;

public static class ViewTypes
{
    public const string MainScreenChange = "MainScreenChange";
    public const string PopupOpen = "PopupOpen";
    public const string PopupClose = "PopupClose";

    public static readonly string[] All = [MainScreenChange, PopupOpen, PopupClose];
}

public class ViewBody
{
    public const int MaxViewNameLength = 60;

    [Required]
    public string ViewType { get; set; } = "";

    [Required, StringLength(MaxViewNameLength)]
    public string ViewName { get; set; } = "";

    private DateTime _ts;
    public DateTime Timestamp
    {
        get => _ts;
        set => _ts = value > DateTime.UtcNow ? DateTime.UtcNow : value;
    }

    [Required]
    public string SessionId { get; set; } = "";

    public SystemProperties SystemProps { get; set; } = new();

    public (bool, string) IsValid(ILogger logger)
    {
        var viewType = CanonicalizeViewType(ViewType);
        if (viewType is null)
            return (false, $"ViewType must be one of {string.Join(", ", ViewTypes.All)}, was: {ViewType}");

        ViewType = viewType;

        if (string.IsNullOrWhiteSpace(ViewName))
            return (false, "ViewName must not be empty.");

        if (ViewName.Length > MaxViewNameLength)
            return (false, $"ViewName must be less than or equal to {MaxViewNameLength} characters, was: {ViewName}");

        return EventBody.ValidateSystemFields(SessionId, Timestamp, SystemProps, logger);
    }

    // Case-insensitively matches the known view types and returns their canonical casing,
    // or null for anything else. Unlike error severities, an unknown type makes the view
    // event meaningless, so it's rejected instead of stored as "".
    private static string? CanonicalizeViewType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return ViewTypes.All.FirstOrDefault(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase));
    }
}
