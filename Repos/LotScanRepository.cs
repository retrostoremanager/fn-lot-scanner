using Microsoft.EntityFrameworkCore;
using fn_lot_scanner.Models;

namespace fn_lot_scanner.Repos;

public class LotScanRepository(LotScanDbContext db) : ILotScanRepository
{
    public async Task<LotScanSession> CreateSessionAsync(Guid id, string employeeId, string photoUrl, CancellationToken ct)
    {
        var session = new LotScanSession
        {
            Id = id,
            EmployeeId = employeeId,
            PhotoUrl = photoUrl,
            Status = LotScanSessionStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.LotScanSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public Task<LotScanSession?> GetSessionAsync(Guid sessionId, CancellationToken ct) =>
        db.LotScanSessions.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task ReplaceItemsAsync(Guid sessionId, IEnumerable<ScannedItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            item.Id = Guid.NewGuid();
            item.SessionId = sessionId;
            item.CreatedAt = DateTimeOffset.UtcNow;
            db.ScannedItems.Add(item);
        }
        await MarkInReviewAsync(sessionId, ct);
    }

    public async Task MarkFailedAsync(Guid sessionId, string errorMessage, CancellationToken ct)
    {
        var session = await db.LotScanSessions.FindAsync([sessionId], ct);
        if (session is null) return;
        session.Status = LotScanSessionStatus.Failed;
        session.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkInReviewAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.LotScanSessions.FindAsync([sessionId], ct);
        if (session is null) return;
        session.Status = LotScanSessionStatus.InReview;
        await db.SaveChangesAsync(ct);
    }

    public Task<ScannedItem?> GetItemAsync(Guid sessionId, Guid itemId, CancellationToken ct) =>
        db.ScannedItems.FirstOrDefaultAsync(i => i.SessionId == sessionId && i.Id == itemId, ct);

    public async Task<ScannedItem> UpdateItemAsync(ScannedItem item, CancellationToken ct)
    {
        db.ScannedItems.Update(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<List<ScannedItem>> BulkSetStateAsync(
        Guid sessionId, IEnumerable<Guid> itemIds, string newState, CancellationToken ct)
    {
        var idSet = itemIds.ToHashSet();
        var items = await db.ScannedItems
            .Where(i => i.SessionId == sessionId && idSet.Contains(i.Id))
            .ToListAsync(ct);

        foreach (var item in items.Where(i => ScannedItemState.BulkEligible.Contains(i.State)))
        {
            item.State = newState;
        }

        await db.SaveChangesAsync(ct);
        return await db.ScannedItems.Where(i => i.SessionId == sessionId).ToListAsync(ct);
    }

    public async Task<(ConfirmResult Result, LotScanSession? Session)> ConfirmAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await GetSessionAsync(sessionId, ct);
        if (session is null) return (ConfirmResult.NotFound, null);
        if (session.Status is LotScanSessionStatus.Confirmed or LotScanSessionStatus.Discarded or LotScanSessionStatus.Failed)
            return (ConfirmResult.AlreadyFinalized, session);

        // FR-009: nothing silently included or excluded -- every item must have been
        // explicitly resolved before confirm is allowed.
        var unresolved = session.Items.Any(i =>
            i.State is ScannedItemState.Pending or ScannedItemState.Unidentified);
        if (unresolved)
            return (ConfirmResult.HasUnresolvedItems, session);

        session.Status = LotScanSessionStatus.Confirmed;
        session.ConfirmedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return (ConfirmResult.Confirmed, session);
    }

    public async Task<DiscardResult> DiscardAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.LotScanSessions.FindAsync([sessionId], ct);
        if (session is null) return DiscardResult.NotFound;
        if (session.Status is LotScanSessionStatus.Confirmed or LotScanSessionStatus.Discarded)
            return DiscardResult.AlreadyFinalized; // idempotent no-op (FR-015)

        session.Status = LotScanSessionStatus.Discarded;
        await db.SaveChangesAsync(ct);
        return DiscardResult.Discarded;
    }
}
