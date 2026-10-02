namespace fn_lot_scanner.Models;

public static class LotScanSessionStatus
{
    public const string Processing = "processing";
    public const string InReview = "in_review";
    public const string Confirmed = "confirmed";
    public const string Discarded = "discarded";
    public const string Failed = "failed";
}

// Mirrors db-lot-scanner/migrations/001_create_lot_scan_sessions_table.sql and
// specs/001-lot-scan-review/data-model.md's LotScanSession entity.
public class LotScanSession
{
    public Guid Id { get; set; }
    public required string EmployeeId { get; set; }
    public string Status { get; set; } = LotScanSessionStatus.Processing;
    public required string PhotoUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    public List<ScannedItem> Items { get; set; } = [];
}
