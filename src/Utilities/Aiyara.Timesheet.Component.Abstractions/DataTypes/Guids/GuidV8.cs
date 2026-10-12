using System.Security.Cryptography;

namespace Aiyara.Timesheet.Component.Abstractions.DataTypes.Guids;

/// <summary>
/// Guid V8 Generator
/// </summary>
public partial class GuidGenerator
{
    /// <summary>
    /// Generate Guid V8
    /// </summary>
    /// <returns></returns>
    public static Guid NewGuid(int regionId, int shardId)
    {
        byte[] uuidBytes = new byte[16];

        uuidBytes[0] = (byte)regionId;
        uuidBytes[1] = (byte)shardId;

        RandomNumberGenerator.Fill(uuidBytes.AsSpan(2, 14));

        // Set version 8 in the high nibble of byte 6, preserving the lower nibble
        uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | (8 << 4));

        // Set RFC 4122 variant bits (10xx xxxx) in byte 8
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80);

        return new Guid(uuidBytes);
    }
}
