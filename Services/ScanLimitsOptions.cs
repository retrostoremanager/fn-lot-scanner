namespace fn_lot_scanner.Services;

// Every photo (first or added) is a billed Claude vision call with no other cost
// control -- the entitlement check (MyStoreBillingClient) is binary, not a budget.
// This caps it per company per day so a pilot store (or a bug causing retries) can't
// run up an unbounded bill.
public class ScanLimitsOptions
{
    public const string SectionName = "ScanLimits";
    public int DailyPhotoCapPerCompany { get; set; } = 40;
}
