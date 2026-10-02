using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Models;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// PATCH /lot-scans/{id}/items:bulk (contracts/lot-scan-api.md). T021.
public class BulkUpdateItems(ILotScanRepository repo)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Function("BulkUpdateItems")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "lot-scans/{sessionId:guid}/items:bulk")]
        HttpRequestData req,
        Guid sessionId, FunctionContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<BulkPatchRequest>(req.Body, JsonOpts, context.CancellationToken);
        if (body is null || body.ItemIds.Count == 0)
            return await ResponseHelper.BadRequest(req, "itemIds must be a non-empty array.");

        if (body.State != ScannedItemState.Accepted && body.State != ScannedItemState.Pending)
            return await ResponseHelper.BadRequest(req, "state must be 'accepted' or 'pending' for bulk updates.");

        // Repo-level scoping rule: only currently pending/accepted items are touched;
        // corrected/excluded/unidentified items pass through untouched (spec Acceptance
        // Scenario 2.2, contracts/lot-scan-api.md scoping rule).
        var items = await repo.BulkSetStateAsync(sessionId, body.ItemIds, body.State, context.CancellationToken);
        return await ResponseHelper.Ok(req, items.Select(ScannedItemResponse.From).ToList());
    }
}
