using fn_lot_scanner.Models;

namespace fn_lot_scanner.Repos;

public enum ConfirmResult { Confirmed, NotFound, HasUnresolvedItems, AlreadyFinalized }
public enum DiscardResult { Discarded, NotFound, AlreadyFinalized }
public enum AddPhotoResult { Added, NotFound, WrongCompany, NotInReview }

// Server-side projection for the history list -- not the API response shape (that's
// SessionSummaryResponse in Services/Dtos), just what the repo hands back.
public record SessionSummary(
    Guid SessionId, string Status, string EmployeeId,
    DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt,
    int PhotoCount, int ItemCount);

public interface ILotScanRepository
{
    Task<LotScanSession> CreateSessionAsync(Guid id, Guid firstPhotoId, string employeeId, int? companyId, string photoUrl, CancellationToken ct);
    Task<LotScanSession?> GetSessionAsync(Guid sessionId, CancellationToken ct);
    Task<List<SessionSummary>> ListSessionsForCompanyAsync(int companyId, string? status, int limit, int offset, CancellationToken ct);

    // Only ever appends -- never deletes existing items, including across multiple
    // photos on the same session.
    Task AppendItemsAsync(Guid sessionId, Guid photoId, IEnumerable<ScannedItem> items, CancellationToken ct);
    Task MarkFailedAsync(Guid sessionId, string errorMessage, CancellationToken ct);
    Task MarkInReviewAsync(Guid sessionId, CancellationToken ct);

    // Adds a photo to an existing session. Guid.Empty's Session is returned alongside
    // the result so callers (AddPhoto.cs) don't need a second fetch for the 403/409
    // response body.
    Task<(AddPhotoResult Result, LotScanSession? Session)> AddPhotoAsync(Guid sessionId, Guid photoId, int? companyId, string photoUrl, CancellationToken ct);
    Task MarkPhotoDoneAsync(Guid photoId, CancellationToken ct);
    Task MarkPhotoFailedAsync(Guid photoId, string errorMessage, CancellationToken ct);

    Task<ScannedItem?> GetItemAsync(Guid sessionId, Guid itemId, CancellationToken ct);
    Task<ScannedItem> UpdateItemAsync(ScannedItem item, CancellationToken ct);

    // Only items currently in ScannedItemState.BulkEligible are affected; others are
    // returned untouched (contracts/lot-scan-api.md scoping rule).
    Task<List<ScannedItem>> BulkSetStateAsync(Guid sessionId, IEnumerable<Guid> itemIds, string newState, CancellationToken ct);

    Task<(ConfirmResult Result, LotScanSession? Session)> ConfirmAsync(Guid sessionId, CancellationToken ct);
    Task<DiscardResult> DiscardAsync(Guid sessionId, CancellationToken ct);
}
