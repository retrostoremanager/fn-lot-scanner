namespace fn_lot_scanner.Services;

// Reused via HTTP, not a direct DB connection (research.md #4).
public class ApiGamedbOptions
{
    public const string SectionName = "ApiGamedb";
    public string BaseUrl { get; set; } = "http://localhost:7072/api";
}
