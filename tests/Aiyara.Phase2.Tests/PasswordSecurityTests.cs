using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aiyara.Phase2.Tests;

public sealed class PasswordSecurityTests
{
    private static readonly EncryptionService Service = new(
        Options.Create(new PasswordGenerateSetting()));

    [Fact]
    public void Argon2id_phc_hash_verifies_and_uses_unique_salts()
    {
        const string password = "Test-only-passphrase-42!";
        var first = Service.HashPassword(password);
        var second = Service.HashPassword(password);

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", first);
        Assert.NotEqual(first, second);
        Assert.True(Service.VerifyPassword(password, first));
        Assert.False(Service.VerifyPassword("different-password", first));
        Assert.False(Service.VerifyPassword(password, first.Replace("m=19456", "m=9999999")));
        Assert.False(Service.VerifyPassword(password, "malformed"));
        Assert.Throws<ArgumentException>(() => Service.HashPassword(new string('é', 600)));
        Assert.False(Service.VerifyPassword(new string('é', 600), first));
    }

    [Fact]
    public void Temporary_passwords_use_every_required_character_group()
    {
        var passwords = Enumerable.Range(0, 20)
            .Select(_ => Service.GenerateTemporaryPassword()).ToArray();
        Assert.Equal(passwords.Length, passwords.Distinct().Count());
        Assert.All(passwords, password =>
        {
            Assert.Equal(16, password.Length);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, character => !char.IsLetterOrDigit(character));
        });
    }

    [Fact]
    public void Invalid_temporary_password_settings_fail_closed()
    {
        var service = new EncryptionService(Options.Create(new PasswordGenerateSetting
        {
            PasswordLength = 4
        }));
        Assert.Throws<InvalidOperationException>(service.GenerateTemporaryPassword);
    }
}
