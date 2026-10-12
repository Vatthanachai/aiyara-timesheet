using System.Security.Cryptography;
using System.Text;

namespace Aiyara.Identities.Services.Authentication;

/// <summary>Encrypts TOTP secrets at rest. A database dump alone must not yield second factors.</summary>
public interface IMfaSecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}

/// <summary>AES-256-GCM with a random 96-bit nonce per value, stored as
/// base64(version | nonce | tag | ciphertext). The key comes from <c>Mfa:EncryptionKey</c>
/// (base64, 32 bytes) and is separate from the token signing key.</summary>
public sealed class AesGcmMfaSecretProtector : IMfaSecretProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] key;

    public AesGcmMfaSecretProtector(string? base64Key)
    {
        byte[]? decoded = null;
        try { decoded = string.IsNullOrWhiteSpace(base64Key) ? null : Convert.FromBase64String(base64Key); }
        catch (FormatException) { }
        if (decoded is not { Length: 32 })
            throw new InvalidOperationException("Mfa:EncryptionKey must be a base64-encoded 32-byte key.");
        key = decoded;
    }

    public string Protect(string secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var output = new byte[1 + NonceSize + TagSize + plaintext.Length];
        output[0] = Version;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(1 + NonceSize + TagSize), output.AsSpan(1 + NonceSize, TagSize));
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedSecret)
    {
        var input = Convert.FromBase64String(protectedSecret);
        if (input.Length < 1 + NonceSize + TagSize || input[0] != Version)
            throw new CryptographicException("Unsupported protected secret.");
        var plaintext = new byte[input.Length - 1 - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(input.AsSpan(1, NonceSize), input.AsSpan(1 + NonceSize + TagSize),
            input.AsSpan(1 + NonceSize, TagSize), plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
