using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CelinesChat.Services.Web;

/// <summary>
/// Code/token generation and the per-IP rate limit for the login endpoint - kept separate from
/// WebRoutes so the actual HTTP plumbing doesn't get lost in crypto/rate-limit details.
/// </summary>
internal static class WebAuth
{
    // How long a single IP has to wait between login attempts, regardless of whether they
    // succeeded - mirrors Chat2's own confirmed-working approach (a ConcurrentDictionary of
    // IP -> next-allowed-tick, checked against Environment.TickCount64) rather than anything more
    // elaborate; this is a LAN convenience feature, not a public-internet-facing login form.
    private const long RateLimitMs = 10_000;

    private static readonly ConcurrentDictionary<string, long> RateLimit = new();

    /// <summary>
    /// A short, easy-to-type-on-a-phone-once code shown in Settings - a cryptographic RNG isn't
    /// needed here (it's a short-lived, user-facing, one-time-entry value, not the actual bearer
    /// credential - see GenerateSessionToken for that), but RandomNumberGenerator is used anyway
    /// since it's just as easy to call and avoids ever having to reason about System.Random's
    /// much weaker guarantees for anything security-adjacent.
    /// </summary>
    public static string GenerateAuthCode() => RandomNumberGenerator.GetInt32(10_000, 100_000).ToString();

    /// <summary>
    /// The actual bearer credential stored in the session cookie - a 30-hex-character
    /// cryptographically random token, deliberately generated with RandomNumberGenerator (not
    /// System.Random) since this is the value that actually grants access for as long as the
    /// cookie lives, not just a one-time code.
    /// </summary>
    public static string GenerateSessionToken() => RandomNumberGenerator.GetHexString(30);

    /// <summary>
    /// True if this source IP is currently allowed to attempt a login - call BEFORE checking the
    /// code itself, and only actually consume the cooldown (via MarkAttempt) once an attempt is
    /// really happening, so merely rendering the login page never itself triggers the limit.
    /// </summary>
    public static bool IsRateLimited(string sourceIp)
    {
        return RateLimit.TryGetValue(sourceIp, out var nextAllowed) && nextAllowed > Environment.TickCount64;
    }

    /// <summary>Records that this IP just attempted a login, starting a fresh cooldown regardless of success/failure.</summary>
    public static void MarkAttempt(string sourceIp)
    {
        RateLimit[sourceIp] = Environment.TickCount64 + RateLimitMs;
    }
}
