using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace fn_lot_scanner.Services;

public class BlobOptions
{
    public const string SectionName = "Blob";
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string ContainerName { get; set; } = "lot-scan-photos";
}

public class PhotoStorageService(IOptions<BlobOptions> options)
{
    private readonly BlobContainerClient _container = new BlobContainerClient(
        options.Value.ConnectionString, options.Value.ContainerName);

    public async Task<string> UploadAsync(Guid sessionId, Stream content, string contentType, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blobName = $"{sessionId}{ExtensionFor(contentType)}";
        var blob = _container.GetBlobClient(blobName);
        await blob.UploadAsync(content, new global::Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return blob.Uri.ToString();
    }

    public async Task<(byte[] Bytes, string ContentType)> DownloadAsync(string photoUrl, CancellationToken ct)
    {
        var blobName = photoUrl[(photoUrl.LastIndexOf('/') + 1)..];
        var blob = _container.GetBlobClient(blobName);
        var download = await blob.DownloadContentAsync(ct);
        return (download.Value.Content.ToArray(), download.Value.Details.ContentType);
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
    };
}
