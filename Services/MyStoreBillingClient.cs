using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace fn_lot_scanner.Services;

// Checks whether a company has the Lot Scanner Stripe add-on active, by calling
// fn-mystore's GET billing/subscription and forwarding the SAME already-validated
// Bearer token -- both apps trust the identical JWT scheme (JwtAuthenticationMiddleware
// was ported from fn-mystore's), so no separate service-to-service secret is needed.
// Fails closed (treats errors/unreachable fn-mystore as "not entitled") since this gates
// a paid feature -- allowing free access on an outage is the worse failure mode here.
public class MyStoreBillingClient(HttpClient httpClient, ILogger<MyStoreBillingClient> logger)
{
    private const string LotScannerAddonType = "lot_scanner";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Keyed by companyId -- avoids a round trip to fn-mystore on every single scan.
    private static readonly ConcurrentDictionary<int, (bool Active, DateTime ExpiresAtUtc)> Cache = new();

    public async Task<bool> HasLotScannerAddOnAsync(int companyId, string bearerToken, CancellationToken ct)
    {
        if (Cache.TryGetValue(companyId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            return cached.Active;

        var active = await FetchFromMyStoreAsync(bearerToken, ct);
        Cache[companyId] = (active, DateTime.UtcNow.Add(CacheTtl));
        return active;
    }

    private async Task<bool> FetchFromMyStoreAsync(string bearerToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "billing/subscription");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("fn-mystore billing/subscription returned {StatusCode}", response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadFromJsonAsync<ApiResponseDto>(
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
            var addOns = body?.Data?.AddOns ?? [];
            return addOns.Any(a => a.AddonType == LotScannerAddonType && a.Active);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check Lot Scanner add-on entitlement via fn-mystore");
            return false;
        }
    }

    private sealed class ApiResponseDto
    {
        public bool Success { get; set; }
        public SubscriptionDetailDto? Data { get; set; }
    }

    private sealed class SubscriptionDetailDto
    {
        public List<AddOnStatusDto> AddOns { get; set; } = [];
    }

    private sealed class AddOnStatusDto
    {
        public string AddonType { get; set; } = string.Empty;
        public bool Active { get; set; }
    }
}
