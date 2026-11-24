using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CelinesChat.Services.Web;

internal static class WebAuth
{
    private const long RateLimitMs = 10_000;

    private static readonly ConcurrentDictionary<string, long> RateLimit = new();

    public static string GenerateAuthCode() => RandomNumberGenerator.GetInt32(10_000, 100_000).ToString();

    public static string GenerateSessionToken() => RandomNumberGenerator.GetHexString(30);

    // Now that the login code can be a user-chosen password rather than always a short throwaway
    // numeric one, a plain != comparison's early-exit-on-first-mismatched-character timing isn't
    // worth leaving as-is just because it was fine for a 5-digit code. FixedTimeEquals takes the
    // same time regardless of where (or whether) the inputs differ - the 10s per-IP rate limit
    // already makes any such timing attack impractically slow either way, but this is a one-line
    // change to remove the concern rather than merely mitigate it.
    public static bool CodeMatches(string candidate, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(actual));

    public static bool IsRateLimited(string sourceIp)
    {
        return RateLimit.TryGetValue(sourceIp, out var nextAllowed) && nextAllowed > Environment.TickCount64;
    }

    public static void MarkAttempt(string sourceIp)
    {
        RateLimit[sourceIp] = Environment.TickCount64 + RateLimitMs;
    }
}
