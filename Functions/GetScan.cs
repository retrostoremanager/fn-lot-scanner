using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// GET /lot-scans/{id} (contracts/lot-scan-api.md). T019.
public class GetScan(ILotScanRepository repo)
{
    [Function("GetScan")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "lot-scans/{id:guid}")] HttpRequestData req,
        Guid id, FunctionContext context)
    {
        var session = await repo.GetSessionAsync(id, context.CancellationToken);
        if (session is null) return await ResponseHelper.NotFound(req);
        return await ResponseHelper.Ok(req, SessionResponse.From(session));
    }
}
