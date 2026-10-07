using System.Diagnostics.Metrics;

namespace Aiyara.Identities.Api;

internal static class AuthenticationMetrics
{
    private static readonly Meter Meter = new("Aiyara.Identities.Authentication");
    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        "aiyara.authentication.attempts", description: "Authentication endpoint outcomes.");

    public static void Record(string action, string outcome) =>
        Attempts.Add(1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("outcome", outcome));
}
