namespace Aiyara.Identities.Services.Authentication;

/// <summary>The caller's User-Agent for the request in flight, set by a middleware so a new refresh
/// session can remember which device it belongs to.</summary>
public sealed class SessionDeviceContext
{
    public const int MaxLength = 300;

    private string? userAgent;

    public string? UserAgent
    {
        get => userAgent;
        set => userAgent = string.IsNullOrWhiteSpace(value) ? null
            : value.Length > MaxLength ? value[..MaxLength] : value;
    }
}

/// <summary>Turns a User-Agent into a short label such as "Chrome on Windows". It is a hint for the
/// member to recognise a sign-in, not an identification.</summary>
public static class SessionDeviceName
{
    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown device";
        var browser = userAgent switch
        {
            _ when userAgent.Contains("Edg/") || userAgent.Contains("EdgA/") => "Edge",
            _ when userAgent.Contains("OPR/") => "Opera",
            _ when userAgent.Contains("Firefox/") || userAgent.Contains("FxiOS/") => "Firefox",
            _ when userAgent.Contains("Chrome/") || userAgent.Contains("CriOS/") => "Chrome",
            _ when userAgent.Contains("Safari/") => "Safari",
            _ => null
        };
        var system = userAgent switch
        {
            _ when userAgent.Contains("Windows") => "Windows",
            _ when userAgent.Contains("iPhone") || userAgent.Contains("iPad") => "iOS",
            _ when userAgent.Contains("Android") => "Android",
            _ when userAgent.Contains("Mac OS X") || userAgent.Contains("Macintosh") => "macOS",
            _ when userAgent.Contains("CrOS") => "ChromeOS",
            _ when userAgent.Contains("Linux") => "Linux",
            _ => null
        };
        return (browser, system) switch
        {
            (not null, not null) => $"{browser} on {system}",
            (not null, null) => browser,
            (null, not null) => system,
            _ => "Unknown device"
        };
    }
}
