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
        [QueueTrigger("lot-scan-identify")] string messageBase64,
        FunctionContext context)
    {
        var json = Convert.FromBase64String(messageBase64);
        var message = JsonSerializer.Deserialize<IdentifyQueueMessage>(json)
            ?? throw new InvalidOperationException("Could not deserialize IdentifyQueueMessage.");

        try
        {
            var (bytes, contentType) = await photos.DownloadAsync(message.PhotoUrl, context.CancellationToken);
            var items = await identification.IdentifyAsync(bytes, contentType, context.CancellationToken);
            await repo.ReplaceItemsAsync(message.SessionId, items, context.CancellationToken);
        }
        catch (IdentificationFailedException ex)
        {
            logger.LogWarning(ex, "Identification failed for session {SessionId}", message.SessionId);
            await repo.MarkFailedAsync(message.SessionId, ex.Message, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error identifying session {SessionId}", message.SessionId);
            await repo.MarkFailedAsync(message.SessionId, "Unexpected error during identification.", context.CancellationToken);
        }
    }
}
