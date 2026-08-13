using System.Net.Http.Headers;
using System.Text.Json;

internal sealed record HttpCacheMetadata(string? ETag, string? LastModified)
{
    internal bool IsEmpty => string.IsNullOrWhiteSpace(ETag) && string.IsNullOrWhiteSpace(LastModified);

    internal static HttpCacheMetadata? FromResponse(HttpResponseMessage response)
    {
        string? etag = response.Headers.ETag?.ToString();
        string? lastModified = response.Content.Headers.LastModified?.ToString("R");

        HttpCacheMetadata metadata = new(etag, lastModified);
        return metadata.IsEmpty ? null : metadata;
    }
}

internal static class HttpCacheMetadataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<HttpCacheMetadata?> ReadAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<HttpCacheMetadata>(stream, JsonOptions, cancellationToken);
        }
        catch
        {
            // A stale/corrupt cache should not prevent a fresh download.
            return null;
        }
    }

    internal static async Task WriteAsync(
        string? path,
        HttpCacheMetadata? metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (metadata is null || metadata.IsEmpty)
        {
            TryDelete(path);
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only; preserve the original write exception.
        }
    }
}
