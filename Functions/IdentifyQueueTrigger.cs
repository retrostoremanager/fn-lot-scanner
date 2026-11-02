using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using fn_lot_scanner.Models;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// Queue-triggered rather than inline in CreateScan's HTTP response -- the AI call can
// take up to ~10s (research.md #3) and isolated-worker HTTP functions shouldn't block
// the response on it; the client polls GET /lot-scans/{id} instead (contract). T017,
// T019 (failure handling).
public class IdentifyQueueTrigger(
    ILotScanRepository repo,
    IdentificationService identification,
    PhotoStorageService photos,
    ILogger<IdentifyQueueTrigger> logger)
{
    [Function("IdentifyQueueTrigger")]
    public async Task Run(
        [QueueTrigger("lot-scan-identify")] string messageJson,
        FunctionContext context)
    {
        // Plain JSON -- the queue client (Program.cs) is configured with
        // QueueMessageEncoding.Base64, so the Functions queue-trigger extension
        // delivers already-decoded text here. No manual decoding needed.
        IdentifyQueueMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize<IdentifyQueueMessage>(messageJson)
                ?? throw new InvalidOperationException("Could not deserialize IdentifyQueueMessage.");

            var (bytes, contentType) = await photos.DownloadAsync(message.PhotoUrl, context.CancellationToken);
            var items = await identification.IdentifyAsync(bytes, contentType, context.CancellationToken);
            await repo.AppendItemsAsync(message.SessionId, message.PhotoId, items, context.CancellationToken);
            await repo.MarkPhotoDoneAsync(message.PhotoId, context.CancellationToken);
            // Idempotent even for an additional photo on an already-in_review session --
            // re-setting in_review on an already-in_review session is a harmless no-op.
            await repo.MarkInReviewAsync(message.SessionId, context.CancellationToken);
            logger.LogInformation(
                "Identification complete: session {SessionId}, photo {PhotoId}, {ItemCount} items detected",
                message.SessionId, message.PhotoId, items.Count);
        }
        catch (IdentificationFailedException ex)
        {
            logger.LogWarning(ex, "Identification failed for session {SessionId}", message?.SessionId);
            if (message is not null)
                await HandleFailureAsync(message, ex.Message, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error identifying session {SessionId}", message?.SessionId);
            if (message is not null)
                await HandleFailureAsync(message, "Unexpected error during identification.", context.CancellationToken);
            else
                throw; // no SessionId to mark failed -- a wire-format bug, let it poison.
        }
    }

    // A session's first/only pending photo failing still fails the whole session (today's
    // original behavior). An *additional* photo failing on an already-in_review session must
    // not touch the session's existing accepted items -- only that one photo is marked failed.
    private async Task HandleFailureAsync(IdentifyQueueMessage message, string errorMessage, CancellationToken ct)
    {
        var session = await repo.GetSessionAsync(message.SessionId, ct);
        if (session is not null && session.Status == LotScanSessionStatus.InReview)
            await repo.MarkPhotoFailedAsync(message.PhotoId, errorMessage, ct);
        else
            await repo.MarkFailedAsync(message.SessionId, errorMessage, ct);
    }
}
