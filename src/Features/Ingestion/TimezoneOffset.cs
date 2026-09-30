namespace Aptabase.Features.Ingestion;

// The player's local UTC offset in minutes (e.g. 120 for UTC+2, -300 for UTC-5), sent by the SDK
// with every event so DST changes are picked up. It's optional, older SDKs don't send it.
public static class TimezoneOffset
{
    // UTC-12:00 to UTC+14:00 are the furthest offsets in use
    public const int MinMinutes = -720;
    public const int MaxMinutes = 840;

    // Returns null when the offset is missing or out of range. An invalid offset
    // is ignored rather than rejecting the whole event.
    public static short? Normalize(int? value)
    {
        if (value is null || value < MinMinutes || value > MaxMinutes)
            return null;

        return (short)value.Value;
    }
}
