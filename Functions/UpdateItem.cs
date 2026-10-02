using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Models;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// PATCH /lot-scans/{id}/items/{itemId} (contracts/lot-scan-api.md).
// Covers: correct-via-catalog (T024, FR-006), price override on any item (T026,
// FR-014), manual entry for unmatched items (T027, FR-008), and exclude (T028).
public class UpdateItem(ILotScanRepository repo, ApiGamedbClient gamedbClient)
{
    private static readonly HashSet<string> AllowedStates =
        [ScannedItemState.Accepted, ScannedItemState.Corrected, ScannedItemState.Excluded, ScannedItemState.Unidentified];
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Function("UpdateItem")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "lot-scans/{sessionId:guid}/items/{itemId:guid}")]
        HttpRequestData req,
        Guid sessionId, Guid itemId, FunctionContext context)
    {
        var item = await repo.GetItemAsync(sessionId, itemId, context.CancellationToken);
        if (item is null) return await ResponseHelper.NotFound(req);

        var body = await JsonSerializer.DeserializeAsync<PatchItemRequest>(req.Body, JsonOpts, context.CancellationToken);
        if (body is null) return await ResponseHelper.BadRequest(req, "Invalid request body.");

        if (body.State is not null && !AllowedStates.Contains(body.State))
            return await ResponseHelper.BadRequest(req, $"state must be one of: {string.Join(", ", AllowedStates)}.");

        if (!string.IsNullOrWhiteSpace(body.FinalCatalogGameId))
        {
            // Catalog-correction path: title/platform/variant always come from the
            // catalog, never the client, once a catalog id is given.
            var resolved = await gamedbClient.ResolveByIdAsync(body.FinalCatalogGameId, context.CancellationToken);
            if (resolved is null)
                return await ResponseHelper.BadRequest(req, $"Unknown catalog game id: {body.FinalCatalogGameId}");

            item.FinalCatalogGameId = body.FinalCatalogGameId;
            item.FinalTitle = resolved.Title;
            item.FinalPlatform = resolved.Platform;
            item.FinalVariant = resolved.Variant;
            item.FinalPrice = body.FinalPrice ?? resolved.Price; // FR-014: client override wins if given
            item.ManuallyEntered = false;
            item.State = body.State ?? ScannedItemState.Corrected;
        }
        else if (!string.IsNullOrWhiteSpace(body.FinalTitle))
        {
            // Manual-entry path (FR-008): no catalog match at all.
            item.FinalCatalogGameId = null;
            item.FinalTitle = body.FinalTitle;
            item.FinalPlatform = body.FinalPlatform;
            item.FinalVariant = body.FinalVariant;
            item.FinalPrice = body.FinalPrice;
            item.ManuallyEntered = true;
            item.State = body.State ?? ScannedItemState.Corrected;
        }
        else if (body.FinalPrice is not null)
        {
            // Pure price override on an already-resolved item (FR-014) -- leave the
            // identity fields as they are.
            item.FinalPrice = body.FinalPrice;
            if (body.State is not null) item.State = body.State;
        }
        else if (body.State is not null)
        {
            // Plain accept/exclude with no field changes (e.g. simple per-card accept).
            item.State = body.State;
        }

        var updated = await repo.UpdateItemAsync(item, context.CancellationToken);
        return await ResponseHelper.Ok(req, ScannedItemResponse.From(updated));
    }
}
