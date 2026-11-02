namespace fn_lot_scanner.Models;

public static class LotScanPhotoStatus
{
    public const string Processing = "processing";
    public const string Done = "done";
    public const string Failed = "failed";
}

// Mirrors db-lot-scanner/migrations/005_add_company_scoping_and_multi_photo.sql's
// lot_scan_photos table. A session starts with one and can gain more while still
// in_review; each photo's own identify status is tracked here so an additional photo
// failing never fails the whole session's existing accepted items.
public class LotScanPhoto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public required string PhotoUrl { get; set; }
    public string Status { get; set; } = LotScanPhotoStatus.Processing;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public LotScanSession? Session { get; set; }
}
