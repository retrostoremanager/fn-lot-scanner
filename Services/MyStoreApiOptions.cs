namespace fn_lot_scanner.Services;

// Reused via HTTP, not a direct DB connection -- same pattern as ApiGamedbOptions.
public class MyStoreApiOptions
{
    public const string SectionName = "MyStoreApi";
    public string BaseUrl { get; set; } = "http://localhost:7073/api";
}
