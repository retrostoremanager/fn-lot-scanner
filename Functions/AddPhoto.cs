using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Middleware;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// POST /lot-scans/{id}/photos -- adds another photo to a scan that's still in_review
// (e.g. a customer brings more items from the same visit). Mirrors CreateScan's
// entitlement gate, multipart extraction, blob upload and enqueue pattern closely.
public class AddPhoto(
    ILotScanRepository repo, PhotoStorageService photos, QueueClient identifyQueue,
    MyStoreBillingClient billing, ILogger<AddPhoto> logger)
{
    [Function("AddPhoto")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "lot-scans/{id:guid}/photos")] HttpRequestData req,
        Guid id, FunctionContext context)
    {
        var principal = context.Features.Get<JwtPrincipalFeature>();
        if (principal is null)
            return await ResponseHelper.BadRequest(req, "Authenticated employee identity is required.");

        // Every photo is a paid Claude call, same as CreateScan.
        var bearerToken = TryGetBearerToken(req);
        var entitled = principal.CompanyId is int companyId && bearerToken is not null
            && await billing.HasLotScannerAddOnAsync(companyId, bearerToken, context.CancellationToken);
        if (!entitled)
        {
            var res = req.CreateResponse(System.Net.HttpStatusCode.PaymentRequired);
            await res.WriteAsJsonAsync(new { error = "The Lot Scanner add-on is not active for this company." });
            return res;
        }

        var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues)
            ? ctValues.FirstOrDefault() ?? ""
            : "";

        var photo = await MultipartHelper.ExtractPhotoAsync(req.Body, contentType, context.CancellationToken);
        if (photo is null)
            return await ResponseHelper.BadRequest(req, "multipart/form-data with a 'photo' part is required.");

        var photoId = Guid.NewGuid();
        var photoUrl = await photos.UploadAsync(
            id, photoId, new MemoryStream(photo.Value.Bytes), photo.Value.ContentType, context.CancellationToken);

        var (result, session) = await repo.AddPhotoAsync(id, photoId, principal.CompanyId, photoUrl, context.CancellationToken);
        switch (result)
        {
            case AddPhotoResult.NotFound:
                return await ResponseHelper.NotFound(req);
            case AddPhotoResult.WrongCompany:
                return await ResponseHelper.Forbidden(req, "This scan does not belong to your company.");
            case AddPhotoResult.NotInReview:
                return await ResponseHelper.Conflict(req, $"Scan is '{session!.Status}'; photos can only be added while in_review.");
        }

        var message = new IdentifyQueueMessage { SessionId = id, PhotoId = photoId, PhotoUrl = photoUrl };
        await identifyQueue.SendMessageAsync(JsonSerializer.Serialize(message), context.CancellationToken);

        logger.LogInformation(
            "Photo added: session {SessionId}, photo {PhotoId}, by employee {EmployeeId}",
            id, photoId, principal.EmployeeId);

        return await ResponseHelper.Accepted(req, new CreateScanResponse { SessionId = id, Status = session!.Status });
    }

    private static string? TryGetBearerToken(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Authorization", out var values)) return null;
        var header = values.FirstOrDefault();
        const string prefix = "bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return header[prefix.Length..].Trim();
    }
}
