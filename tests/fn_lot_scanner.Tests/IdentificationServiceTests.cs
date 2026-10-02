using fn_lot_scanner.Models;
using fn_lot_scanner.Services;
using fn_lot_scanner.Services.Dtos;
using FluentAssertions;
using Xunit;

namespace fn_lot_scanner.Tests;

// Covers research.md #3/#6's confidence heuristic (tasks.md T029).
public class IdentificationServiceTests
{
    [Fact]
    public void ToScannedItem_NoMatch_IsUnidentified()
    {
        var guess = new VisionGuessDto { Title = "Some Obscure Import", Platform = "PC Engine" };

        var item = IdentificationService.ToScannedItem(guess, match: null);

        item.State.Should().Be(ScannedItemState.Unidentified);
        item.Confidence.Should().BeNull();
        item.SuggestedTitle.Should().Be("Some Obscure Import");
    }

    [Fact]
    public void ToScannedItem_MatchedNotMatchedFlag_IsUnidentified()
    {
        var guess = new VisionGuessDto { Title = "Chrono Trigger" };
        var match = new BulkLookupResultDto { Title = "Chrono Trigger", Matched = false };

        var item = IdentificationService.ToScannedItem(guess, match);

        item.State.Should().Be(ScannedItemState.Unidentified);
    }

    [Fact]
    public void ToScannedItem_ExactTitleMatch_GetsHighConfidence()
    {
        var guess = new VisionGuessDto { Title = "Chrono Trigger", Platform = "SNES" };
        var match = new BulkLookupResultDto
        {
            Title = "Chrono Trigger",
            Matched = true,
            GameId = 42,
            MatchedTitle = "Chrono Trigger",
            System = "Super Nintendo",
            LooseBuyCents = 2500
        };

        var item = IdentificationService.ToScannedItem(guess, match);

        item.State.Should().Be(ScannedItemState.Pending);
        item.Confidence.Should().Be(0.9m);
        item.SuggestedCatalogGameId.Should().Be("42");
        item.SuggestedPrice.Should().Be(25.00m);
    }

    [Fact]
    public void ToScannedItem_FuzzyTitleMatch_GetsLowConfidenceBelowDefaultThreshold()
    {
        var guess = new VisionGuessDto { Title = "Chrono Trigga" }; // typo from the vision model
        var match = new BulkLookupResultDto
        {
            Title = "Chrono Trigga",
            Matched = true,
            GameId = 42,
            MatchedTitle = "Chrono Trigger",
            LooseBuyCents = 2500
        };

        var item = IdentificationService.ToScannedItem(guess, match);

        item.Confidence.Should().Be(0.55m);
        item.Confidence.Should().BeLessThan(new AnthropicOptions().LowConfidenceThreshold);
    }

    [Fact]
    public void ToScannedItem_NoPriceOnMatch_LeavesSuggestedPriceNull()
    {
        var guess = new VisionGuessDto { Title = "Chrono Trigger" };
        var match = new BulkLookupResultDto
        {
            Title = "Chrono Trigger", Matched = true, GameId = 42, MatchedTitle = "Chrono Trigger", LooseBuyCents = null
        };

        var item = IdentificationService.ToScannedItem(guess, match);

        item.SuggestedPrice.Should().BeNull();
    }
}
