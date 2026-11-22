using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace fn_lot_scanner.Helpers;

public enum PhotoExtractError
{
    None,
    NoPhotoPart,
    TooLarge,
}

public readonly record struct PhotoExtractResult(byte[]? Bytes, string? ContentType, PhotoExtractError Error)
{
    public bool IsSuccess => Error == PhotoExtractError.None;
}

public static class MultipartHelper
{
    // Bounds both the Claude vision payload size (cost scales with image size) and blob
    // storage footprint. A real phone photo is typically 2-8MB; 15MB leaves headroom for
    // a high-res shot without allowing an effectively-unbounded (and potentially
    // very expensive or abusive) upload.
    public const long MaxPhotoBytes = 15 * 1024 * 1024;

    // Extracts the "photo" part from a multipart/form-data request body
    // (POST /lot-scans per contracts/lot-scan-api.md).
    public static async Task<PhotoExtractResult> ExtractPhotoAsync(
        Stream body, string contentType, CancellationToken ct)
    {
        var boundary = HeaderUtilities.RemoveQuotes(
            MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
            return new PhotoExtractResult(null, null, PhotoExtractError.NoPhotoPart);

        var reader = new MultipartReader(boundary, body);
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) != null)
        {
            var disposition = section.GetContentDispositionHeader();
            if (disposition is null || !disposition.IsFileDisposition()) continue;
            if (disposition.Name.Value != "photo") continue;

            using var ms = new MemoryStream();
            // Copy in bounded chunks so an oversized upload is rejected as soon as it
            // crosses the limit, instead of buffering the whole thing into memory first.
            var buffer = new byte[81920];
            int read;
            while ((read = await section.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (ms.Length + read > MaxPhotoBytes)
                    return new PhotoExtractResult(null, null, PhotoExtractError.TooLarge);
                await ms.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            return new PhotoExtractResult(ms.ToArray(), section.ContentType ?? "image/jpeg", PhotoExtractError.None);
        }
        return new PhotoExtractResult(null, null, PhotoExtractError.NoPhotoPart);
    }
}
