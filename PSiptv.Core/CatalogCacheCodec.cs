using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PSiptv.Core;

public static class CatalogCacheCodec
{
    public static byte[] Encode(IReadOnlyList<MediaItem> items, byte[] key, string context)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(gzip, items);

        if (!compressed.TryGetBuffer(out var buffer))
            throw new InvalidOperationException("Não foi possível preparar a cache do catálogo.");

        try
        {
            return AccountDataProtection.ProtectData(
                buffer.AsSpan(0, checked((int)compressed.Length)), key, context);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, checked((int)compressed.Length)));
        }
    }

    public static IReadOnlyList<MediaItem> Decode(byte[] payload, byte[] key, string context)
    {
        var data = AccountDataProtection.UnprotectData(payload, key, context);
        try
        {
            if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                using var input = new MemoryStream(data, writable: false);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                return JsonSerializer.Deserialize<List<MediaItem>>(gzip) ?? [];
            }

            // Caches created before compression was introduced contain raw JSON.
            return JsonSerializer.Deserialize<List<MediaItem>>(data) ?? [];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
