using fn_lot_scanner.Models;

namespace fn_lot_scanner.Repos;

public enum ConfirmResult { Confirmed, NotFound, HasUnresolvedItems, AlreadyFinalized }
public enum DiscardResult { Discarded, NotFound, AlreadyFinalized }

public interface ILotScanRepository
{
    Task<LotScanSession> CreateSessionAsync(Guid id, string employeeId, string photoUrl, CancellationToken ct);
    Task<LotScanSession?> GetSessionAsync(Guid sessionId, CancellationToken ct);
    Task ReplaceItemsAsync(Guid sessionId, IEnumerable<ScannedItem> items, CancellationToken ct);
    Task MarkFailedAsync(Guid sessionId, string errorMessage, CancellationToken ct);
    Task MarkInReviewAsync(Guid sessionId, CancellationToken ct);

    Task<ScannedItem?> GetItemAsync(Guid sessionId, Guid itemId, CancellationToken ct);
    Task<ScannedItem> UpdateItemAsync(ScannedItem item, CancellationToken ct);

    // Only items currently in ScannedItemState.BulkEligible are affected; others are
    // returned untouched (contracts/lot-scan-api.md scoping rule).
    Task<List<ScannedItem>> BulkSetStateAsync(Guid sessionId, IEnumerable<Guid> itemIds, string newState, CancellationToken ct);

    Task<(ConfirmResult Result, LotScanSession? Session)> ConfirmAsync(Guid sessionId, CancellationToken ct);
    Task<DiscardResult> DiscardAsync(Guid sessionId, CancellationToken ct);
}
