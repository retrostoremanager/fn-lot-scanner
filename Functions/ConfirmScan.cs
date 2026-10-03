using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using fn_lot_scanner.Helpers;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Functions;

// POST /lot-scans/{id}/confirm (contracts/lot-scan-api.md). T022.
public class ConfirmScan(ILotScanRepository repo, ILogger<ConfirmScan> logger)
{
    [Function("ConfirmScan")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "lot-scans/{id:guid}/confirm")] HttpRequestData req,
        Guid id, FunctionContext context)
    {
        var (result, session) = await repo.ConfirmAsync(id, context.CancellationToken);

        if (result == ConfirmResult.Confirmed)
        {
            // FR-011 audit trail: who confirmed this lot and when.
            logger.LogInformation(
                "Scan confirmed: session {SessionId} by employee {EmployeeId} with {ItemCount} items",
                session!.Id, session.EmployeeId, session.Items.Count(i => i.State is "accepted" or "corrected"));
        }
        else
        {
            logger.LogWarning("Confirm rejected for session {SessionId}: {Result}", id, result);
        }

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
