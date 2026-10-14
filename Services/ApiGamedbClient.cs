using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using fn_lot_scanner.Services.Dtos;

namespace fn_lot_scanner.Services;

// Calls api-gamedb's real pricing/lookup + pricing/bulk-lookup endpoints (confirmed by
// reading api-gamedb's PricingLookupFunctions.cs directly). Both endpoints are
// AuthorizationLevel.Anonymous, so no function-key/auth header is needed.
public class ApiGamedbClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Backs fn-lot-scanner's own GET /lot-scans/catalog-search?q= proxy (FR-006).
    public async Task<List<PriceLookupCandidateDto>> SearchAsync(string query, string? platform, int limit, CancellationToken ct)
    {
        var qs = $"title={HttpUtility.UrlEncode(query)}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(platform)) qs += $"&platform={HttpUtility.UrlEncode(platform)}";

        var response = await httpClient.GetAsync($"pricing/lookup?{qs}", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PriceLookupResponseDto>(JsonOpts, ct);
        return body?.Candidates ?? [];
    }

    public record ResolvedGame(string Title, string? Platform, string? Variant, decimal? Price);

    // Resolves a specific, already-known catalog id (employee picked it from search
    // results) to canonical fields + a buy price, by combining GET /games/{id} with a
    // single-item bulk-lookup (api-gamedb has no by-id price endpoint directly).
    public async Task<ResolvedGame?> ResolveByIdAsync(string catalogGameId, CancellationToken ct)
    {
        var gameResponse = await httpClient.GetAsync($"games/{catalogGameId}", ct);
        if (!gameResponse.IsSuccessStatusCode) return null;
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDto>(JsonOpts, ct);
        if (game is null) return null;

        var matches = await BulkMatchAsync([(game.Title, game.System?.Name)], ct);
        var price = matches.FirstOrDefault(m => m.GameId == game.Id)?.LooseBuyCents;

        return new ResolvedGame(game.Title, game.System?.Name, game.Variant?.Name,
            price.HasValue ? price.Value / 100m : null);
    }

    // One batched call resolves every AI-detected item against the catalog in a single
    // round trip (research.md #3/#4) and returns a margin-adjusted buy price per match.
    public async Task<List<BulkLookupResultDto>> BulkMatchAsync(
        IEnumerable<(string Title, string? Platform)> guesses, CancellationToken ct)
    {
        var items = guesses.Select(g => new BulkLookupItemDto { Title = g.Title, Platform = g.Platform }).ToList();
        if (items.Count == 0) return [];

        // Pre-serialized StringContent, not PostAsJsonAsync -- PostAsJsonAsync streams
        // the body via JsonContent without a known Content-Length, which api-gamedb's
        // isolated-worker HttpRequestData.ReadAsStringAsync() was silently truncating to
        // empty for real (19-item) request bodies, causing JSON deserialization to throw
        // on api-gamedb's side and return 400 "Body must be a JSON array..." even though
        // the data itself was always well-formed (confirmed 2026-10-14: synthetic curl
        // tests with an explicit Content-Length never reproduced it; only the streamed
        // HttpClient path did). A fully-buffered StringContent always has a known
        // Content-Length, avoiding chunked transfer entirely.
        var json = JsonSerializer.Serialize(items);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("pricing/bulk-lookup", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BulkLookupResponseDto>(JsonOpts, ct);
        return body?.Results ?? [];
    }
}
