using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Microsoft.Extensions.Options;

namespace Aiyara.Timesheet.Component.Abstractions.Securities;

public sealed class EncryptionService(IOptions<PasswordGenerateSetting> passwordSetting)
    : IEncryptionService
{
    private const int MemoryKiB = 19 * 1024;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#%^&*()-_=+[]";

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (Encoding.UTF8.GetByteCount(password) > 1024)
            throw new ArgumentException("Password is too long.", nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Derive(password, salt, MemoryKiB, Iterations, Parallelism);
        return $"$argon2id$v=19$m={MemoryKiB},t={Iterations},p={Parallelism}${ToPhcBase64(salt)}${ToPhcBase64(hash)}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(passwordHash) ||
            passwordHash.Length > 256 || Encoding.UTF8.GetByteCount(password) > 1024)
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id" ||
            parts[2] != "v=19")
        {
            return false;
        }

        var parameters = parts[3].Split(',');
        if (parameters.Length != 3 || !TryParameter(parameters[0], "m=", out var memory) ||
            !TryParameter(parameters[1], "t=", out var iterations) ||
            !TryParameter(parameters[2], "p=", out var parallelism) ||
            memory is < MemoryKiB or > 131072 || iterations is < 2 or > 10 ||
            parallelism is < 1 or > 4)
        {
            return false;
        }

        try
        {
            var salt = FromPhcBase64(parts[4]);
            var expected = FromPhcBase64(parts[5]);
            if (salt.Length != SaltLength || expected.Length != HashLength) return false;
            var actual = Derive(password, salt, memory, iterations, parallelism);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string GenerateTemporaryPassword()
    {
        var options = passwordSetting.Value;
        return GenerateTemporaryPassword(options.PasswordLength,
            options.IsIncludeLowerCase, options.IsIncludeUpperCase,
            options.IsIncludeNumeric, options.IsIncludeSpecialCase);
    }

    public string GenerateTemporaryPassword(int length, bool requireLowercase,
        bool requireUppercase, bool requireDigit, bool requireSymbol)
    {
        var required = new List<string>();
        if (requireLowercase) required.Add(Lowercase);
        if (requireUppercase) required.Add(Uppercase);
        if (requireDigit) required.Add(Digits);
        if (requireSymbol) required.Add(Symbols);
        if (required.Count == 0) required.Add(Lowercase + Uppercase + Digits + Symbols);
        if (length < Math.Max(12, required.Count) || length > 128)
        {
            throw new InvalidOperationException("Temporary password settings are invalid.");
        }

        var alphabet = string.Concat(required);
        var characters = new char[length];
        for (var i = 0; i < required.Count; i++)
        {
            var group = required[i];
            characters[i] = group[RandomNumberGenerator.GetInt32(group.Length)];
        }
        for (var i = required.Count; i < characters.Length; i++)
        {
            characters[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        for (var i = characters.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }
        return new string(characters);
    }

    private static byte[] Derive(string password, byte[] salt, int memory, int iterations,
        int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon2.GetBytes(HashLength);
    }

    private static bool TryParameter(string value, string prefix, out int parsed)
    {
        parsed = 0;
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(value.AsSpan(prefix.Length), out parsed);
    }

    private static string ToPhcBase64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static byte[] FromPhcBase64(string value)
        => Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
}
