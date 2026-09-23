namespace Aptabase.Features.Privacy;

// Persistent user ID sent by the SDK: a salted SHA-256 hash (hex) computed client-side,
// stable across sessions and devices. It's optional, older SDKs don't send it.
public static class PersistentUserId
{
    // Intentionally vague, we don't want to hint at what a valid ID looks like
    public const string InvalidMessage = "UserID is invalid";

    private const int HashLength = 64;

    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (value.Length != HashLength)
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        normalized = value.ToLowerInvariant();
        return true;
    }
}
