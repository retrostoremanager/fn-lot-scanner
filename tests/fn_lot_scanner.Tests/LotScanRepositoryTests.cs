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
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", 1, "blob://x", default);

        var pending = new ScannedItem { State = ScannedItemState.Pending };
        var alreadyCorrected = new ScannedItem { State = ScannedItemState.Corrected, FinalPrice = 10m };
        var alreadyExcluded = new ScannedItem { State = ScannedItemState.Excluded };
        await repo.AppendItemsAsync(session.Id, Guid.NewGuid(), [pending, alreadyCorrected, alreadyExcluded], default);

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
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", 1, "blob://x", default);
        await repo.AppendItemsAsync(session.Id, Guid.NewGuid(),
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
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", 1, "blob://x", default);
        await repo.AppendItemsAsync(session.Id, Guid.NewGuid(),
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
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", 1, "blob://x", default);
        await repo.AppendItemsAsync(session.Id, Guid.NewGuid(), [new ScannedItem { State = ScannedItemState.Accepted, FinalPrice = 5m }], default);
        await repo.ConfirmAsync(session.Id, default);

        var result = await repo.DiscardAsync(session.Id, default);

        result.Should().Be(DiscardResult.AlreadyFinalized);
        (await repo.GetSessionAsync(session.Id, default))!.Status.Should().Be(LotScanSessionStatus.Confirmed); // not overwritten
    }

    [Fact]
    public async Task ListSessionsForCompanyAsync_OnlyReturnsSessionsForThatCompany()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var ours = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", companyId: 1, "blob://a", default);
        await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-2", companyId: 2, "blob://b", default);

        var results = await repo.ListSessionsForCompanyAsync(1, status: null, limit: 50, offset: 0, default);

        results.Should().ContainSingle(s => s.SessionId == ours.Id);
    }

    [Fact]
    public async Task ListSessionsForCompanyAsync_ReportsPhotoAndItemCounts()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", companyId: 1, "blob://a", default);
        await repo.AppendItemsAsync(session.Id, Guid.NewGuid(),
            [new ScannedItem { State = ScannedItemState.Pending }, new ScannedItem { State = ScannedItemState.Pending }], default);
        await repo.MarkInReviewAsync(session.Id, default); // AddPhotoAsync requires in_review
        await repo.AddPhotoAsync(session.Id, Guid.NewGuid(), companyId: 1, "blob://b", default);

        var results = await repo.ListSessionsForCompanyAsync(1, status: null, limit: 50, offset: 0, default);

        var summary = results.Single(s => s.SessionId == session.Id);
        summary.PhotoCount.Should().Be(2); // the session's original photo + the added one
        summary.ItemCount.Should().Be(2);
    }

    [Fact]
    public async Task AddPhotoAsync_RejectsWhenSessionIsNotInReview()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        // Freshly created sessions start as 'processing', not 'in_review'.
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", companyId: 1, "blob://a", default);

        var (result, _) = await repo.AddPhotoAsync(session.Id, Guid.NewGuid(), companyId: 1, "blob://b", default);

        result.Should().Be(AddPhotoResult.NotInReview);
    }

    [Fact]
    public async Task AddPhotoAsync_RejectsWhenSessionBelongsToAnotherCompany()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", companyId: 1, "blob://a", default);
        await repo.MarkInReviewAsync(session.Id, default);

        var (result, _) = await repo.AddPhotoAsync(session.Id, Guid.NewGuid(), companyId: 2, "blob://b", default);

        result.Should().Be(AddPhotoResult.WrongCompany);
    }

    [Fact]
    public async Task AddPhotoAsync_SucceedsWhileInReviewAndSameCompany()
    {
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", companyId: 1, "blob://a", default);
        await repo.MarkInReviewAsync(session.Id, default);

        var (result, _) = await repo.AddPhotoAsync(session.Id, Guid.NewGuid(), companyId: 1, "blob://b", default);

        result.Should().Be(AddPhotoResult.Added);
        (await repo.GetSessionAsync(session.Id, default))!.Photos.Should().HaveCount(2);
    }

    [Fact]
    public async Task MarkPhotoFailedAsync_DoesNotTouchSessionStatusOrExistingItems()
    {
        // Covers the IdentifyQueueTrigger failure-isolation rule: an *additional* photo
        // failing on an already-in_review session must leave the session and its
        // existing accepted items alone -- only that one photo is marked failed.
        using var db = NewDb();
        var repo = new LotScanRepository(db);
        var session = await repo.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), "emp-1", companyId: 1, "blob://a", default);
        await repo.AppendItemsAsync(session.Id, Guid.NewGuid(), [new ScannedItem { State = ScannedItemState.Accepted, FinalPrice = 5m }], default);
        await repo.MarkInReviewAsync(session.Id, default);

        var (addResult, _) = await repo.AddPhotoAsync(session.Id, Guid.NewGuid(), companyId: 1, "blob://b", default);
        addResult.Should().Be(AddPhotoResult.Added);
        var secondPhoto = (await repo.GetSessionAsync(session.Id, default))!.Photos.Single(p => p.PhotoUrl == "blob://b");

        await repo.MarkPhotoFailedAsync(secondPhoto.Id, "AI call timed out", default);

        var reloaded = await repo.GetSessionAsync(session.Id, default);
        reloaded!.Status.Should().Be(LotScanSessionStatus.InReview); // untouched
        reloaded.Items.Should().ContainSingle(i => i.State == ScannedItemState.Accepted); // untouched
        reloaded.Photos.Single(p => p.Id == secondPhoto.Id).Status.Should().Be(LotScanPhotoStatus.Failed);
    }
}
