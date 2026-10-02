namespace fn_lot_scanner.Services;

public class AnthropicOptions
{
    public const string SectionName = "Anthropic";
    public string ApiKey { get; set; } = "";
    public int MaxTokens { get; set; } = 2048;

    // research.md #6: starting default, tunable without a spec/plan change.
    public decimal LowConfidenceThreshold { get; set; } = 0.7m;
}
