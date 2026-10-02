using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using fn_lot_scanner.Models;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Services;

public class IdentificationFailedException(string message, Exception? inner = null) : Exception(message, inner);

// research.md #3: Claude vision detects/guesses candidate items; api-gamedb's
// pricing/bulk-lookup resolves each guess to a canonical catalog entry + buy price in
// one batched call. research.md #6: confidence is primarily the catalog match quality,
// NOT a numeric score from the vision model -- api-gamedb's bulk-lookup only returns a
// Matched boolean + best-candidate row, so "fuzzy-match score" in practice means the
// three-tier heuristic below, not a continuous value.
public class IdentificationService(
    AnthropicClient anthropicClient,
    ApiGamedbClient gamedbClient,
    IOptions<AnthropicOptions> options,
    ILogger<IdentificationService> logger)
{
    private const decimal ExactMatchConfidence = 0.9m;
    private const decimal FuzzyMatchConfidence = 0.55m; // below the 0.7 default threshold -> flagged low-confidence

    public async Task<List<ScannedItem>> IdentifyAsync(byte[] photoBytes, string mediaType, CancellationToken ct)
    {
        List<VisionGuessDto> guesses;
        try
        {
            guesses = await GetVisionGuessesAsync(photoBytes, mediaType, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IdentificationFailedException("AI vision call failed or returned an unparseable result.", ex);
        }

        if (guesses.Count == 0)
            return [];

        var matches = await gamedbClient.BulkMatchAsync(guesses.Select(g => (g.Title, g.Platform)), ct);

        var items = new List<ScannedItem>(guesses.Count);
        for (var i = 0; i < guesses.Count; i++)
        {
            var guess = guesses[i];
            var match = i < matches.Count ? matches[i] : null;
            items.Add(ToScannedItem(guess, match));
        }
        return items;
    }

    // Internal (not private) so the confidence heuristic is unit-testable without
    // mocking the Anthropic/api-gamedb calls (see AssemblyInfo.cs InternalsVisibleTo).
    internal static ScannedItem ToScannedItem(VisionGuessDto guess, BulkLookupResultDto? match)
    {
        if (match is null || !match.Matched)
        {
            return new ScannedItem
            {
                SuggestedTitle = guess.Title,
                SuggestedPlatform = guess.Platform,
                SuggestedVariant = guess.Variant,
                State = ScannedItemState.Unidentified
            };
        }

        var exact = string.Equals(match.MatchedTitle, guess.Title, StringComparison.OrdinalIgnoreCase);
        var priceCents = match.LooseBuyCents; // default to the loose/cart-only buy price (FR-013: no condition estimate for v1)

        return new ScannedItem
        {
            SuggestedCatalogGameId = match.GameId?.ToString(),
            SuggestedTitle = match.MatchedTitle ?? guess.Title,
            SuggestedPlatform = match.System ?? guess.Platform,
            SuggestedVariant = match.Variant,
            SuggestedPrice = priceCents.HasValue ? priceCents.Value / 100m : null,
            Confidence = exact ? ExactMatchConfidence : FuzzyMatchConfidence,
            State = ScannedItemState.Pending
        };
    }

    private async Task<List<VisionGuessDto>> GetVisionGuessesAsync(byte[] photoBytes, string mediaType, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(photoBytes);
        var messages = new List<Message>
        {
            new()
            {
                Role = RoleType.User,
                Content = new List<ContentBase>
                {
                    new ImageContent { Source = new ImageSource { MediaType = mediaType, Data = base64 } },
                    new TextContent
                    {
                        Text = "This photo shows a lot of physical video games a customer brought into a " +
                               "retro game store. Identify every distinct physical game you can see " +
                               "(cartridge, disc, or case). Respond with ONLY a JSON array, no prose, no " +
                               "markdown code fences, in this exact shape: " +
                               "[{\"title\": \"<game title>\", \"platform\": \"<console/system name or null>\", " +
                               "\"variant\": \"<special edition/variant or null>\"}]. " +
                               "One array entry per physical item. If you cannot make out an item at all, omit it."
                    }
                }
            }
        };

        var parameters = new MessageParameters
        {
            Messages = messages,
            MaxTokens = options.Value.MaxTokens,
            Model = AnthropicModels.Claude46Sonnet,
            Stream = false,
            Temperature = 0m // deterministic extraction, not creative generation
        };

        var result = await anthropicClient.Messages.GetClaudeMessageAsync(parameters, ct);
        var text = result.Message?.ToString() ?? "";
        return ParseGuesses(text);
    }

    private List<VisionGuessDto> ParseGuesses(string text)
    {
        var jsonText = ExtractJsonArray(text);
        try
        {
            return JsonSerializer.Deserialize<List<VisionGuessDto>>(
                jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not parse vision model output as JSON: {Text}", text);
            throw new IdentificationFailedException("AI vision response was not valid JSON.", ex);
        }
    }

    // Claude sometimes wraps JSON in ```json fences despite instructions; strip them.
    private static string ExtractJsonArray(string text)
    {
        var trimmed = text.Trim();
        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }
}
