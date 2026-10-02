namespace fn_lot_scanner.Services.Dtos;

// Raw shape Claude is prompted to return for each detected item -- one per physical
// game in the lot photo, before catalog resolution.
public class VisionGuessDto
{
    public string Title { get; set; } = "";
    public string? Platform { get; set; }
    public string? Variant { get; set; }
}
