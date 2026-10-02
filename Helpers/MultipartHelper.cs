using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace fn_lot_scanner.Helpers;

public static class MultipartHelper
{
    // Extracts the "photo" part from a multipart/form-data request body
    // (POST /lot-scans per contracts/lot-scan-api.md).
    public static async Task<(byte[] Bytes, string ContentType)?> ExtractPhotoAsync(
        Stream body, string contentType, CancellationToken ct)
    {
        var boundary = HeaderUtilities.RemoveQuotes(
            MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        if (string.IsNullOrEmpty(boundary)) return null;

        var reader = new MultipartReader(boundary, body);
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) != null)
        {
            var disposition = section.GetContentDispositionHeader();
            if (disposition is null || !disposition.IsFileDisposition()) continue;
            if (disposition.Name.Value != "photo") continue;

            using var ms = new MemoryStream();
            await section.Body.CopyToAsync(ms, ct);
            return (ms.ToArray(), section.ContentType ?? "image/jpeg");
        }
        return null;
    }
}
