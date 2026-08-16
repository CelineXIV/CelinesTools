using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CelinesChat.Services.Web;

internal static class WebAuth
{
    private const long RateLimitMs = 10_000;

    private static readonly ConcurrentDictionary<string, long> RateLimit = new();

    public static string GenerateAuthCode() => RandomNumberGenerator.GetInt32(10_000, 100_000).ToString();

    public static string GenerateSessionToken() => RandomNumberGenerator.GetHexString(30);

    public static bool IsRateLimited(string sourceIp)
    {
        return RateLimit.TryGetValue(sourceIp, out var nextAllowed) && nextAllowed > Environment.TickCount64;
    }

    public static void MarkAttempt(string sourceIp)
    {
        RateLimit[sourceIp] = Environment.TickCount64 + RateLimitMs;
    }
}
