using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// POST /lot-scans/{id}/confirm (contracts/lot-scan-api.md). T022.
public class ConfirmScan(ILotScanRepository repo)
{
    [Function("ConfirmScan")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "lot-scans/{id:guid}/confirm")] HttpRequestData req,
        Guid id, FunctionContext context)
    {
        var (result, session) = await repo.ConfirmAsync(id, context.CancellationToken);

        return result switch
        {
            ConfirmResult.NotFound => await ResponseHelper.NotFound(req),
            ConfirmResult.HasUnresolvedItems => await ResponseHelper.Conflict(req,
                "Every item must be accepted, corrected, or excluded before confirming."),
            ConfirmResult.AlreadyFinalized => await ResponseHelper.Conflict(req,
                $"Session is already {session!.Status}."),
            ConfirmResult.Confirmed => await ResponseHelper.Ok(req, new ConfirmResponse
            {
                SessionId = session!.Id,
                Status = session.Status,
                ConfirmedAt = session.ConfirmedAt!.Value,
                Items = session.Items
                    .Where(i => i.State is "accepted" or "corrected")
                    .Select(ScannedItemResponse.From).ToList()
            }),
            _ => await ResponseHelper.Conflict(req, "Unexpected confirm result.")
        };
    }
}
