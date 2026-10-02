using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace fn_lot_scanner.Middleware;

// Ported from fn-mystore's JwtAuthenticationMiddleware so fn-lot-scanner validates the
// exact same employee tokens mystore issues, rather than inventing a second auth scheme
// (plan.md Technical Context: "fn-lot-scanner must resolve a real employee identity
// from it, not a client-supplied value"). Every endpoint in this app requires auth --
// there is no anonymous allow-list, unlike mystore's login/registration endpoints.
public class JwtAuthenticationMiddleware(IConfiguration configuration, ILogger<JwtAuthenticationMiddleware> logger)
    : IFunctionsWorkerMiddleware
{
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpRequest = await context.GetHttpRequestDataAsync();
        if (httpRequest == null)
        {
            await next(context);
            return;
        }

        if (!TryGetTokenFromRequest(httpRequest, out var token))
        {
            await ReturnUnauthorized(context, httpRequest, "Authorization header with Bearer token is required.");
            return;
        }

        if (!_tokenHandler.CanReadToken(token))
        {
            await ReturnUnauthorized(context, httpRequest, "Invalid token format.");
            return;
        }

        var customSecretKey = configuration["JwtAuthentication__SecretKey"]
            ?? configuration["JwtAuthentication:SecretKey"];
        if (!string.IsNullOrEmpty(customSecretKey))
        {
            if (TryValidateCustomJwt(context, token!, customSecretKey))
            {
                await next(context);
                return;
            }
            await ReturnUnauthorized(context, httpRequest, "Invalid or expired token.");
            return;
        }

        var authority = configuration["EntraExternalId__Authority"];
        var audience = configuration["EntraExternalId__ClientId"];
        var companyIdClaim = configuration["EntraExternalId__CompanyIdClaim"] ?? "extension_CompanyId";

        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(audience))
        {
            logger.LogError("JWT authentication is not configured for {FunctionName}.", context.FunctionDefinition.Name);
            await ReturnUnauthorized(context, httpRequest, "Invalid or expired token.");
            return;
        }

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        var validationParameters = new TokenValidationParameters
        {
            ValidAudience = audience,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        try
        {
            var openIdConfig = await configManager.GetConfigurationAsync(context.CancellationToken);
            validationParameters.ValidIssuer = openIdConfig.Issuer;
            validationParameters.IssuerSigningKeys = openIdConfig.SigningKeys;

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            SetPrincipalFeature(context, principal, companyIdClaim);
            await next(context);
        }
        catch (SecurityTokenExpiredException)
        {
            await ReturnUnauthorized(context, httpRequest, "Token has expired.");
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "Token validation failed for {FunctionName}", context.FunctionDefinition.Name);
            await ReturnUnauthorized(context, httpRequest, "Invalid or expired token.");
        }
    }

    private bool TryValidateCustomJwt(FunctionContext context, string token, string secretKey)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidIssuer = "MyStore",
                ValidAudience = "MyStore",
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            var companyIdClaim = configuration["EntraExternalId__CompanyIdClaim"] ?? "extension_CompanyId";
            SetPrincipalFeature(context, principal, companyIdClaim);
            return true;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            logger.LogWarning(ex, "JWT validation failed for {FunctionName}", context.FunctionDefinition.Name);
            return false;
        }
    }

    private static void SetPrincipalFeature(FunctionContext context, ClaimsPrincipal principal, string companyIdClaim)
    {
        var employeeId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new SecurityTokenException("Token has no subject/nameidentifier claim to use as employee identity.");

        var companyIdValue = principal.FindFirst(companyIdClaim)?.Value;
        var companyId = int.TryParse(companyIdValue, out var cid) ? cid : (int?)null;

        context.Features.Set(new JwtPrincipalFeature(principal, employeeId, companyId));
    }

    private static bool TryGetTokenFromRequest(Microsoft.Azure.Functions.Worker.Http.HttpRequestData? request, out string? token)
    {
        token = null;
        if (request?.Headers == null) return false;
        if (!request.Headers.TryGetValues("Authorization", out var authValues) || authValues == null) return false;

        var authHeader = authValues.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader)) return false;

        const string bearerPrefix = "bearer ";
        if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        token = authHeader[bearerPrefix.Length..].Trim();
        return !string.IsNullOrEmpty(token);
    }

    private static async Task ReturnUnauthorized(FunctionContext context, Microsoft.Azure.Functions.Worker.Http.HttpRequestData request, string message)
    {
        var response = request.CreateResponse(HttpStatusCode.Unauthorized);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("WWW-Authenticate", "Bearer");
        await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message }));
        context.GetInvocationResult().Value = response;
    }
}
