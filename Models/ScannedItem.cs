namespace fn_lot_scanner.Models;

public static class ScannedItemState
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Corrected = "corrected";
    public const string Excluded = "excluded";
    public const string Unidentified = "unidentified";

    // The only states a bulk select-all/deselect-all action may target
    // (contracts/lot-scan-api.md scoping rule; protects an already-corrected or
    // excluded item from being silently reverted).
    public static readonly HashSet<string> BulkEligible = [Pending, Accepted];
}

// Mirrors db-lot-scanner/migrations/002_create_scanned_items_table.sql and
// specs/001-lot-scan-review/data-model.md's ScannedItem entity.
public class ScannedItem
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }

    public string? SuggestedCatalogGameId { get; set; }
    public string? SuggestedTitle { get; set; }
    public string? SuggestedPlatform { get; set; }
    public string? SuggestedVariant { get; set; }
    public decimal? SuggestedPrice { get; set; }

    // Primarily the catalog fuzzy-match score; vision self-report is a secondary
    // signal only where available (research.md #3/#6).
    public decimal? Confidence { get; set; }

    public string? FinalCatalogGameId { get; set; }
    public string? FinalTitle { get; set; }
    public string? FinalPlatform { get; set; }
    public string? FinalVariant { get; set; }
    public decimal? FinalPrice { get; set; }

    public string State { get; set; } = ScannedItemState.Pending;
    public bool ManuallyEntered { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public LotScanSession? Session { get; set; }
}
