using System.Buffers;

namespace Aiyara.Identities.Api;

/// <summary>Reads an uploaded image up to a size limit and accepts only JPEG, PNG, or WebP,
/// identified by their bytes rather than the client's file name or content type.</summary>
public static class ImageUploads
{
    public static async Task<(byte[] Bytes, string ContentType)?> ReadAsync(IFormFile file, int maxBytes,
        CancellationToken ct)
    {
        if (file.Length is < 1 || file.Length > maxBytes) return null;
        await using var input = file.OpenReadStream();
        await using var content = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                if (content.Length + read > maxBytes) return null;
                await content.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        var bytes = content.ToArray();
        return DetectType(bytes) is { } contentType ? (bytes, contentType) : null;
    }

    public static string? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[]
            { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
