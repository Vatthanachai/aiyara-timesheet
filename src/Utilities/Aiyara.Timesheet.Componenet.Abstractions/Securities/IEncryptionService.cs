namespace Aiyara.Timesheet.Component.Abstractions.Securities;

public interface IEncryptionService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
    string GenerateTemporaryPassword();
    string GenerateTemporaryPassword(int length, bool requireLowercase,
        bool requireUppercase, bool requireDigit, bool requireSymbol);
}
