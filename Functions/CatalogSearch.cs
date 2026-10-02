using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Services;

namespace fn_lot_scanner.Functions;

// GET /lot-scans/catalog-search?q= (contracts/lot-scan-api.md, FR-006). T025.
// Thin proxy to api-gamedb's real pricing/lookup endpoint.
public class CatalogSearch(ApiGamedbClient gamedbClient)
{
    [Function("CatalogSearch")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "lot-scans/catalog-search")] HttpRequestData req,
        FunctionContext context)
    {
        var query = req.Query["q"];
        if (string.IsNullOrWhiteSpace(query))
            return await ResponseHelper.BadRequest(req, "q is required.");

        var platform = req.Query["platform"];
        var candidates = await gamedbClient.SearchAsync(query, platform, 10, context.CancellationToken);
        return await ResponseHelper.Ok(req, candidates);
    }
}
