using System.Security.Cryptography;
using System.Text;

namespace Aiyara.Identities.Services.Authentication;

/// <summary>RFC 6238 time-based one-time passwords: HMAC-SHA1, 30-second steps, six digits,
/// which is what common authenticator apps expect.</summary>
public static class Totp
{
    public const int StepSeconds = 30;
    private const int Digits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new 160-bit secret, Base32-encoded without padding.</summary>
    public static string GenerateSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(string base32Secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(FromBase32(base32Secret), counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Accepts a code from the current step or one step either side, but only for a
    /// step later than <paramref name="lastUsedStep"/>, so a code cannot be replayed.</summary>
    public static bool TryVerify(string base32Secret, string? code, DateTimeOffset now,
        long lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        var normalized = code?.Replace(" ", "", StringComparison.Ordinal).Trim();
        if (normalized is not { Length: Digits } || !normalized.All(char.IsAsciiDigit)) return false;
        var current = StepAt(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastUsedStep) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(base32Secret, step)),
                    Encoding.ASCII.GetBytes(normalized)))
            {
                matchedStep = step;
                return true;
            }
        }
        return false;
    }

    /// <summary>The otpauth:// URI that authenticator apps import, labelled with the account.</summary>
    public static string ProvisioningUri(string issuer, string accountName, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}" +
        $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    private static string ToBase32(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    private static byte[] FromBase32(string encoded)
    {
        var output = new List<byte>(encoded.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var character in encoded.TrimEnd('=').ToUpperInvariant())
        {
            var value = Base32Alphabet.IndexOf(character);
            if (value < 0) throw new FormatException("The secret is not valid Base32.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
