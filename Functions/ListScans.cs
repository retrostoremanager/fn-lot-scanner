using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Middleware;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// GET /lot-scans?status=&limit=&offset= -- store-scoped scan history (any employee at a
// company sees all of it, matching how the rest of RetroStoreManager isn't siloed per
// employee). Fails closed on a missing CompanyId claim, same stance as CreateScan.
public class ListScans(ILotScanRepository repo)
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    [Function("ListScans")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "lot-scans")] HttpRequestData req,
        FunctionContext context)
    {
        var principal = context.Features.Get<JwtPrincipalFeature>();
        if (principal is null)
            return await ResponseHelper.BadRequest(req, "Authenticated employee identity is required.");
        if (principal.CompanyId is not int companyId)
            return await ResponseHelper.BadRequest(req, "No company is associated with this account.");

        var status = req.Query["status"];
        var limit = int.TryParse(req.Query["limit"], out var l) ? Math.Clamp(l, 1, MaxLimit) : DefaultLimit;
        var offset = int.TryParse(req.Query["offset"], out var o) ? Math.Max(o, 0) : 0;

        var summaries = await repo.ListSessionsForCompanyAsync(companyId, status, limit, offset, context.CancellationToken);
        return await ResponseHelper.Ok(req, summaries.Select(SessionSummaryResponse.From).ToList());
    }
}
