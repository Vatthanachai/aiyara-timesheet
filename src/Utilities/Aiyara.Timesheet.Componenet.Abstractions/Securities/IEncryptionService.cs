namespace Aiyara.Timesheet.Component.Abstractions.Securities;

/// <summary>
/// Interface for password hashing, verification, and generation services
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Hashes a password with a random salt using PBKDF2
    /// </summary>
    /// <param name="plainText">The password to hash</param>
    /// <param name="salt">The generated salt for the password</param>
    /// <returns>The hashed password as a hex string</returns>
    string HashPassword(string plainText, out byte[] salt);

    /// <summary>
    /// Verifies a password against a stored hash and salt
    /// </summary>
    /// <param name="plainText">The password to verify</param>
    /// <param name="hash">The stored hash to verify against</param>
    /// <param name="salt">The salt used to create the hash</param>
    /// <returns>True if the password matches the hash, false otherwise</returns>
    bool VerifyPassword(string plainText, string hash, string salt);

    /// <summary>
    /// Generates a strong password based on configured settings
    /// </summary>
    /// <returns>A randomly generated strong password</returns>
    string PasswordGenerate();


    /// <summary>
    /// Combine Password component
    /// </summary>
    /// <param name="hash"></param>
    /// <param name="salt"></param>
    /// <returns></returns>
    string CombinePasswordComponents(string hash, string salt);

    /// <summary>
    /// Extracts hash, salt, and iteration count from a combined password string
    /// </summary>
    /// <param name="combinedPassword">The combined password string in format: hash|salt|iteration</param>
    /// <param name="hash">Output parameter for the extracted hash</param>
    /// <param name="salt">Output parameter for the extracted salt</param>
    /// <param name="iteration">Output parameter for the extracted iteration count</param>
    /// <returns>True if extraction was successful, false otherwise</returns>
    bool TryExtract(string combinedPassword, out string hash, out string salt, out int iteration);

    /// <summary>
    /// Extracts hash, salt, and iteration count from a combined password string
    /// </summary>
    /// <param name="combinedPassword">The combined password string in format: hash|salt|iteration</param>
    /// <returns>A tuple containing (hash, salt, iteration)</returns>
    /// <exception cref="ArgumentException">Thrown when the combined password format is invalid</exception>
    (string Hash, string Salt, int Iteration) Extract(string combinedPassword);
}