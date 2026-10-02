using fn_lot_scanner.Models;

namespace fn_lot_scanner.Services.Dtos;

// Wire shapes matching specs/001-lot-scan-review/contracts/lot-scan-api.md exactly.

public class ScannedItemResponse
{
    public Guid ItemId { get; set; }
    public string State { get; set; } = "";
    public string? SuggestedTitle { get; set; }
    public string? SuggestedPlatform { get; set; }
    public string? SuggestedVariant { get; set; }
    public decimal? SuggestedPrice { get; set; }
    public decimal? Confidence { get; set; }
    public string? FinalCatalogGameId { get; set; }
    public string? FinalTitle { get; set; }
    public string? FinalPlatform { get; set; }
    public string? FinalVariant { get; set; }
    public decimal? FinalPrice { get; set; }
    public bool ManuallyEntered { get; set; }

    public static ScannedItemResponse From(ScannedItem i) => new()
    {
        ItemId = i.Id,
        State = i.State,
        SuggestedTitle = i.SuggestedTitle,
        SuggestedPlatform = i.SuggestedPlatform,
        SuggestedVariant = i.SuggestedVariant,
        SuggestedPrice = i.SuggestedPrice,
        Confidence = i.Confidence,
        FinalCatalogGameId = i.FinalCatalogGameId,
        FinalTitle = i.FinalTitle,
        FinalPlatform = i.FinalPlatform,
        FinalVariant = i.FinalVariant,
        FinalPrice = i.FinalPrice,
        ManuallyEntered = i.ManuallyEntered
    };
}

public class SessionResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public List<ScannedItemResponse> Items { get; set; } = [];

    public static SessionResponse From(LotScanSession s) => new()
    {
        SessionId = s.Id,
        Status = s.Status,
        ErrorMessage = s.ErrorMessage,
        Items = s.Items.Select(ScannedItemResponse.From).ToList()
    };
}

public class CreateScanResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = "";
}

public class PatchItemRequest
{
    public string? State { get; set; }
    public string? FinalCatalogGameId { get; set; }
    public string? FinalTitle { get; set; }
    public string? FinalPlatform { get; set; }
    public string? FinalVariant { get; set; }
    public decimal? FinalPrice { get; set; }
}

public class BulkPatchRequest
{
    public string State { get; set; } = "";
    public List<Guid> ItemIds { get; set; } = [];
}

public class ConfirmResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset ConfirmedAt { get; set; }
    public List<ScannedItemResponse> Items { get; set; } = [];
}

public class DiscardResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = "";
}
