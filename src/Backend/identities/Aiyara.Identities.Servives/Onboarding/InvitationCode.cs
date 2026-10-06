using System.Security.Cryptography;
using System.Text;

namespace Aiyara.Identities.Services.Onboarding;

public static class InvitationCode
{
    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string code) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
