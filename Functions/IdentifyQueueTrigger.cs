using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
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
        IdentifyQueueMessage? message = null;
        try
        {
            // Plain JSON, not base64 -- see CreateScan.cs for why. Deserialization is
            // inside the try/catch now so any future parsing issue also degrades
            // gracefully instead of silently poisoning the queue with no session update.
            message = JsonSerializer.Deserialize<IdentifyQueueMessage>(messageJson)
                ?? throw new InvalidOperationException("Could not deserialize IdentifyQueueMessage.");

            var (bytes, contentType) = await photos.DownloadAsync(message.PhotoUrl, context.CancellationToken);
            var items = await identification.IdentifyAsync(bytes, contentType, context.CancellationToken);
            await repo.ReplaceItemsAsync(message.SessionId, items, context.CancellationToken);
            logger.LogInformation(
                "Identification complete: session {SessionId}, {ItemCount} items detected",
                message.SessionId, items.Count);
        }
        catch (IdentificationFailedException ex)
        {
            logger.LogWarning(ex, "Identification failed for session {SessionId}", message?.SessionId);
            if (message is not null)
                await repo.MarkFailedAsync(message.SessionId, ex.Message, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error identifying session {SessionId}", message?.SessionId);
            // If message is null, deserialization itself failed -- there's no SessionId to
            // mark failed. That genuinely is unrecoverable and should go to the poison
            // queue (it would indicate a sender/wire-format bug, not a per-item failure).
            if (message is not null)
                await repo.MarkFailedAsync(message.SessionId, "Unexpected error during identification.", context.CancellationToken);
            else
                throw;
        }
    }
}
