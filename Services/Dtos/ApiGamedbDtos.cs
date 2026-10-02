namespace fn_lot_scanner.Services.Dtos;

// Mirrors api-gamedb's Services/Dtos/PricingLookupDtos.cs wire shape (camelCase JSON;
// api-gamedb's ResponseHelper uses JsonNamingPolicy.CamelCase). Only the fields
// fn-lot-scanner actually uses are included.

public class PriceLookupCandidateDto
{
    public int GameId { get; set; }
    public string Title { get; set; } = "";
    public string? System { get; set; }
    public string? Variant { get; set; }
    public int? LooseMedianCents { get; set; }
    public int? CompleteMedianCents { get; set; }
}

public class PriceLookupResponseDto
{
    public int Count { get; set; }
    public List<PriceLookupCandidateDto> Candidates { get; set; } = [];
}

public class BulkLookupItemDto
{
    public string? Title { get; set; }
    public string? Platform { get; set; }
}

public class BulkLookupResultDto
{
    public string Title { get; set; } = "";
    public string? Platform { get; set; }
    public bool Matched { get; set; }
    public int? GameId { get; set; }
    public string? MatchedTitle { get; set; }
    public string? System { get; set; }
    public string? Variant { get; set; }
    public int? LooseMarketCents { get; set; }
    public int? LooseBuyCents { get; set; }
    public int? CompleteMarketCents { get; set; }
    public int? CompleteBuyCents { get; set; }
}

public class BulkLookupResponseDto
{
    public List<BulkLookupResultDto> Results { get; set; } = [];
}

// Mirrors api-gamedb's GameDto / GameSystemDto / NamedRefDto (Functions/GameFunctions.cs),
// trimmed to the fields fn-lot-scanner needs for a by-id catalog resolution.
public class GameDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public NamedRefDto? System { get; set; }
    public NamedRefDto? Variant { get; set; }
}

public class NamedRefDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
