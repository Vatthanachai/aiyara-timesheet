using System.Security.Cryptography;
using Aiyara.Timesheet.Component.Abstractions.DataTypes.Guids;

namespace Aiyara.Identities.Services;

internal static class IdentityGuid
{
    public static Guid New() => GuidGenerator.NewGuid(
        RandomNumberGenerator.GetInt32(1, 10),
        RandomNumberGenerator.GetInt32(1, 10));
}
