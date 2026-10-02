namespace fn_lot_scanner.Services.Dtos;

public class IdentifyQueueMessage
{
    public Guid SessionId { get; set; }
    public string PhotoUrl { get; set; } = "";
}
