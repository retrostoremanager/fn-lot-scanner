using fn_lot_scanner.Models;
using fn_lot_scanner.Repos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace fn_lot_scanner.Tests;

// Covers tasks.md T021 (bulk scoping rule) and T022 (confirm rejection rule).
public class LotScanRepositoryTests
{
    private static LotScanDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LotScanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task BulkSetStateAsync_LeavesCorrectedAndExcludedItemsUntouched()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), "emp-1", "blob://x", default);

        var pending = new ScannedItem { State = ScannedItemState.Pending };
        var alreadyCorrected = new ScannedItem { State = ScannedItemState.Corrected, FinalPrice = 10m };
        var alreadyExcluded = new ScannedItem { State = ScannedItemState.Excluded };
        await repo.ReplaceItemsAsync(session.Id, [pending, alreadyCorrected, alreadyExcluded], default);

        var allIds = new[] { pending.Id, alreadyCorrected.Id, alreadyExcluded.Id };
        var result = await repo.BulkSetStateAsync(session.Id, allIds, ScannedItemState.Accepted, default);

        result.First(i => i.Id == pending.Id).State.Should().Be(ScannedItemState.Accepted);
        result.First(i => i.Id == alreadyCorrected.Id).State.Should().Be(ScannedItemState.Corrected); // untouched
        result.First(i => i.Id == alreadyExcluded.Id).State.Should().Be(ScannedItemState.Excluded); // untouched
    }

    [Fact]
    public async Task ConfirmAsync_RejectsWhenAnyItemIsPendingOrUnidentified()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), "emp-1", "blob://x", default);
        await repo.ReplaceItemsAsync(session.Id,
            [new ScannedItem { State = ScannedItemState.Accepted, FinalPrice = 5m }, new ScannedItem { State = ScannedItemState.Pending }],
            default);

        var (result, _) = await repo.ConfirmAsync(session.Id, default);

        result.Should().Be(ConfirmResult.HasUnresolvedItems);
    }

    [Fact]
    public async Task ConfirmAsync_SucceedsWhenAllItemsResolved()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), "emp-1", "blob://x", default);
        await repo.ReplaceItemsAsync(session.Id,
            [new ScannedItem { State = ScannedItemState.Accepted, FinalPrice = 5m }, new ScannedItem { State = ScannedItemState.Excluded }],
            default);

        var (result, confirmed) = await repo.ConfirmAsync(session.Id, default);

        result.Should().Be(ConfirmResult.Confirmed);
        confirmed!.Status.Should().Be(LotScanSessionStatus.Confirmed);
        confirmed.ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DiscardAsync_IsIdempotentOnAnAlreadyConfirmedSession()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), "emp-1", "blob://x", default);
        await repo.ReplaceItemsAsync(session.Id, [new ScannedItem { State = ScannedItemState.Accepted, FinalPrice = 5m }], default);
        await repo.ConfirmAsync(session.Id, default);

        var result = await repo.DiscardAsync(session.Id, default);

        result.Should().Be(DiscardResult.AlreadyFinalized);
        (await repo.GetSessionAsync(session.Id, default))!.Status.Should().Be(LotScanSessionStatus.Confirmed); // not overwritten
    }
}
