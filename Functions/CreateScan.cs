using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Middleware;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// POST /lot-scans (contracts/lot-scan-api.md). T018.
public class CreateScan(
    ILotScanRepository repo, PhotoStorageService photos, QueueClient identifyQueue,
    MyStoreBillingClient billing, IOptions<ScanLimitsOptions> scanLimits, ILogger<CreateScan> logger)
{
    [Function("CreateScan")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "lot-scans")] HttpRequestData req,
        FunctionContext context)
    {
        var principal = context.Features.Get<JwtPrincipalFeature>();
        if (principal is null)
            return await ResponseHelper.BadRequest(req, "Authenticated employee identity is required.");

        // Every scan triggers a paid Claude vision call -- gate it on the company having
        // the Lot Scanner Stripe add-on active. Fails closed: no CompanyId claim or no
        // reachable fn-mystore both deny rather than allow.
        var bearerToken = TryGetBearerToken(req);
        var entitled = principal.CompanyId is int companyId && bearerToken is not null
            && await billing.HasLotScannerAddOnAsync(companyId, bearerToken, context.CancellationToken);
        if (!entitled)
        {
            var res = req.CreateResponse(System.Net.HttpStatusCode.PaymentRequired);
            await res.WriteAsJsonAsync(new { error = "The Lot Scanner add-on is not active for this company." });
            return res;
        }

        // Each photo is a billed Claude call with no other cost control (the entitlement
        // check above is binary, not a budget) -- cap it per company per rolling 24h.
        var cap = scanLimits.Value.DailyPhotoCapPerCompany;
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var photosToday = await repo.CountPhotosCreatedSinceAsync(principal.CompanyId!.Value, since, context.CancellationToken);
        if (photosToday >= cap)
            return await ResponseHelper.TooManyRequests(req,
                $"This company has reached its limit of {cap} scans per day. Try again later.");

        var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues)
            ? ctValues.FirstOrDefault() ?? ""
            : "";

        var photo = await MultipartHelper.ExtractPhotoAsync(req.Body, contentType, context.CancellationToken);
        if (photo.Error == PhotoExtractError.TooLarge)
            return await ResponseHelper.PayloadTooLarge(req, $"Photo exceeds the {MultipartHelper.MaxPhotoBytes / (1024 * 1024)}MB limit.");
        if (!photo.IsSuccess)
            return await ResponseHelper.BadRequest(req, "multipart/form-data with a 'photo' part is required.");

        // Pre-generate the session/photo ids so the blob can be named after them before the
        // session row (which needs the final photo_url) is created.
        var sessionId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var photoUrl = await photos.UploadAsync(
            sessionId, photoId, new MemoryStream(photo.Bytes!), photo.ContentType!, context.CancellationToken);

        var session = await repo.CreateSessionAsync(
            sessionId, photoId, principal.EmployeeId, principal.CompanyId, photoUrl, context.CancellationToken);

        // Plain JSON string here -- identifyQueue is configured with
        // QueueMessageEncoding.Base64 (see Program.cs), so the SDK handles base64
        // wrapping on the wire to match what the Functions queue-trigger extension
        // expects. No manual encoding on either side of this queue.
        var message = new IdentifyQueueMessage { SessionId = session.Id, PhotoId = photoId, PhotoUrl = photoUrl };
        await identifyQueue.SendMessageAsync(JsonSerializer.Serialize(message), context.CancellationToken);

        logger.LogInformation(
            "Scan created: session {SessionId} by employee {EmployeeId}", session.Id, principal.EmployeeId);

        return await ResponseHelper.Accepted(req, new CreateScanResponse { SessionId = session.Id, Status = session.Status });
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
