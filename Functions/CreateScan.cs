using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Middleware;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// POST /lot-scans (contracts/lot-scan-api.md). T018.
public class CreateScan(ILotScanRepository repo, PhotoStorageService photos, QueueClient identifyQueue)
{
    [Function("CreateScan")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "lot-scans")] HttpRequestData req,
        FunctionContext context)
    {
        var principal = context.Features.Get<JwtPrincipalFeature>();
        if (principal is null)
            return await ResponseHelper.BadRequest(req, "Authenticated employee identity is required.");

        var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues)
            ? ctValues.FirstOrDefault() ?? ""
            : "";

        var photo = await MultipartHelper.ExtractPhotoAsync(req.Body, contentType, context.CancellationToken);
        if (photo is null)
            return await ResponseHelper.BadRequest(req, "multipart/form-data with a 'photo' part is required.");

        // Pre-generate the session id so the blob can be named after it before the
        // session row (which needs the final photo_url) is created.
        var sessionId = Guid.NewGuid();
        var photoUrl = await photos.UploadAsync(
            sessionId, new MemoryStream(photo.Value.Bytes), photo.Value.ContentType, context.CancellationToken);

        var session = await repo.CreateSessionAsync(sessionId, principal.EmployeeId, photoUrl, context.CancellationToken);

        var message = new IdentifyQueueMessage { SessionId = session.Id, PhotoUrl = photoUrl };
        await identifyQueue.SendMessageAsync(
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(message)), context.CancellationToken);

        return await ResponseHelper.Accepted(req, new CreateScanResponse { SessionId = session.Id, Status = session.Status });
    }
}
