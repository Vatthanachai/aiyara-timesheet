using System.Security.Cryptography;
using System.Text;

using Aiyara.Timesheet.Component.Abstractions.Securities.Options;

using Microsoft.Extensions.Options;

namespace Aiyara.Timesheet.Component.Abstractions.Securities;

/// <summary>
/// Implementation of password hashing, verification, and generation services
/// </summary>
public class EncryptionService(IOptions<HashSetting> hashSetting, IOptions<PasswordGenerateSetting> passwordSetting) : IEncryptionService
{
    private readonly HashAlgorithmName _hashAlgorithm = HashAlgorithmName.SHA512;
    private const char ComponentDelimiter = '.';

    /// <summary>
    /// Hashes a password with a random salt using PBKDF2 with SHA-512
    /// </summary>
    /// <param name="plainText">The password to hash</param>
    /// <param name="salt">The generated salt for the password</param>
    /// <returns>The hashed password as a hex string</returns>
    public string HashPassword(string plainText, out byte[] salt)
    {
        salt = RandomNumberGenerator.GetBytes(hashSetting.Value.KeySize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(plainText),
            salt,
            hashSetting.Value.Iteration,
            _hashAlgorithm,
            hashSetting.Value.KeySize);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Verifies a password against a stored hash and salt using PBKDF2 with SHA-512
    /// </summary>
    /// <param name="plainText">The password to verify</param>
    /// <param name="hash">The stored hash to verify against</param>
    /// <param name="salt">The salt used to create the hash</param>
    /// <returns>True if the password matches the hash, false otherwise</returns>
    public bool VerifyPassword(string plainText, string hash, string salt)
    {
        var hashToCompare = Rfc2898DeriveBytes.Pbkdf2(plainText,
            Convert.FromHexString(salt),
            hashSetting.Value.Iteration,
            _hashAlgorithm,
            hashSetting.Value.KeySize);
        return hashToCompare.SequenceEqual(Convert.FromHexString(hash));
    }

    /// <summary>
    /// Generates a strong password based on configured settings
    /// </summary>
    /// <returns>A randomly generated strong password</returns>
    public string PasswordGenerate()
    {
        string lowercaseLetters = "abcdefghijklmnopqrstuvwxyz";
        string uppercaseLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string digits = "0123456789";
        string specialCharacters = "!@#%^&*()-_=+[]|;:,<>/?";

        string allCharacters = string.Empty;
        StringBuilder passwordBuilder = new StringBuilder();

        if (passwordSetting.Value.IsIncludeLowerCase)
        {
            allCharacters += lowercaseLetters;
            passwordBuilder.Append(RandomChar(lowercaseLetters));
        }

        if (passwordSetting.Value.IsIncludeUpperCase)
        {
            allCharacters += uppercaseLetters;
            passwordBuilder.Append(RandomChar(uppercaseLetters));
        }

        if (passwordSetting.Value.IsIncludeNumeric)
        {
            allCharacters += digits;
            passwordBuilder.Append(RandomChar(digits));
        }

        if (passwordSetting.Value.IsIncludeSpecialCase)
        {
            allCharacters += specialCharacters;
            passwordBuilder.Append(RandomChar(specialCharacters));
        }

        Random random = new Random();
        for (int i = passwordBuilder.Length; i < passwordSetting.Value.PasswordLength; i++)
        {
            passwordBuilder.Append(allCharacters[random.Next(allCharacters.Length)]);
        }

        string strongPassword = new string(passwordBuilder.ToString().OrderBy(c => Guid.NewGuid()).ToArray());

        return strongPassword;
    }

    /// <summary>
    /// Combine Password component
    /// </summary>
    /// <param name="hash"></param>
    /// <param name="salt"></param>
    /// <returns></returns>
    public string CombinePasswordComponents(string hash, string salt)
        => Combine(hash, salt, hashSetting.Value.Iteration);

    /// <summary>
    /// Combines hash, salt, and iteration count into a single encrypted password string
    /// </summary>
    /// <param name="hash">The password hash as a hex string</param>
    /// <param name="salt">The salt as a hex string</param>
    /// <param name="iteration">The iteration count used in PBKDF2</param>
    /// <returns>Combined password string in format: hash|salt|iteration</returns>
    /// <exception cref="ArgumentNullException">Thrown when hash or salt is null or empty</exception>
    /// <exception cref="ArgumentException">Thrown when iteration is less than or equal to zero</exception>
    public string Combine(string hash, string salt, int iteration)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentNullException(nameof(hash), "Hash cannot be null or empty");

        if (string.IsNullOrWhiteSpace(salt))
            throw new ArgumentNullException(nameof(salt), "Salt cannot be null or empty");

        return iteration <= 0
            ? throw new ArgumentException("Iteration must be greater than zero", nameof(iteration))
            : $"{hash}{ComponentDelimiter}{salt}{ComponentDelimiter}{iteration}";
    }

    /// <summary>
    /// Extracts hash, salt, and iteration count from a combined password string
    /// </summary>
    /// <param name="combinedPassword">The combined password string in format: hash|salt|iteration</param>
    /// <param name="hash">Output parameter for the extracted hash</param>
    /// <param name="salt">Output parameter for the extracted salt</param>
    /// <param name="iteration">Output parameter for the extracted iteration count</param>
    /// <returns>True if extraction was successful, false otherwise</returns>
    public bool TryExtract(string combinedPassword, out string hash, out string salt, out int iteration)
    {
        hash = string.Empty;
        salt = string.Empty;
        iteration = 0;

        if (string.IsNullOrWhiteSpace(combinedPassword))
            return false;

        var components = combinedPassword.Split(ComponentDelimiter);

        if (components.Length != 3)
            return false;

        hash = components[0];
        salt = components[1];

        if (!int.TryParse(components[2], out iteration))
            return false;

        return !string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(salt) && iteration > 0;
    }

    /// <summary>
    /// Extracts hash, salt, and iteration count from a combined password string
    /// </summary>
    /// <param name="combinedPassword">The combined password string in format: hash|salt|iteration</param>
    /// <returns>A tuple containing (hash, salt, iteration)</returns>
    /// <exception cref="ArgumentException">Thrown when the combined password format is invalid</exception>
    public (string Hash, string Salt, int Iteration) Extract(string combinedPassword)
    {
        return !TryExtract(combinedPassword, out var hash, out var salt, out var iteration)
            ? throw new ArgumentException("Invalid combined password format. Expected format: hash|salt|iteration", nameof(combinedPassword))
            : (hash, salt, iteration);
    }

    /// <summary>
    /// Generates a random character from the specified character set
    /// </summary>
    /// <param name="characterSet">The set of characters to choose from</param>
    /// <returns>A randomly selected character</returns>
    private static char RandomChar(string characterSet)
    {
        Random random = new Random();
        return characterSet[random.Next(characterSet.Length)];
    }
}