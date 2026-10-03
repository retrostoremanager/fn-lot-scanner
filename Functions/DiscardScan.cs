using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// POST /lot-scans/{id}/discard (contracts/lot-scan-api.md, FR-015). T023.
public class DiscardScan(ILotScanRepository repo, ILogger<DiscardScan> logger)
{
    [Function("DiscardScan")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "lot-scans/{id:guid}/discard")] HttpRequestData req,
        Guid id, FunctionContext context)
    {
        var result = await repo.DiscardAsync(id, context.CancellationToken);
        if (result == DiscardResult.NotFound) return await ResponseHelper.NotFound(req);

        logger.LogInformation("Scan discarded: session {SessionId} ({Result})", id, result);

        // Idempotent (FR-015): if it was already confirmed/discarded, report its real
        // current status rather than overwriting the response with "discarded".
        var status = result == DiscardResult.Discarded
            ? "discarded"
            : (await repo.GetSessionAsync(id, context.CancellationToken))?.Status ?? "discarded";

        return await ResponseHelper.Ok(req, new DiscardResponse { SessionId = id, Status = status });
    }
}
