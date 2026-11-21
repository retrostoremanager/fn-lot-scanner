using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;

namespace fn_lot_scanner.Middleware;

// Ported from fn-mystore's CorsMiddleware -- same gap it fixes there: Azure's platform-level
// CORS (az functionapp cors add) doesn't exist when running locally/in a container, and this
// app's web build (app-lot-scanner's Expo web export) calls it directly from a browser.
public class CorsMiddleware(IConfiguration configuration) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpRequest = await context.GetHttpRequestDataAsync();
        if (httpRequest == null)
        {
            await next(context);
            return;
        }

        var origin = GetOrigin(httpRequest);
        var allowedOrigins = GetAllowedOrigins();

        if (httpRequest.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var response = httpRequest.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, origin, allowedOrigins);
            context.GetInvocationResult().Value = response;
            return;
        }

        await next(context);

        var result = context.GetInvocationResult().Value;
        if (result is HttpResponseData responseData)
        {
            AddCorsHeaders(responseData, origin, allowedOrigins);
        }
    }

    private static string? GetOrigin(HttpRequestData request)
    {
        if (request.Headers.TryGetValues("Origin", out var values))
        {
            return values?.FirstOrDefault();
        }
        return null;
    }

    private HashSet<string> GetAllowedOrigins()
    {
        // "Cors__AllowedOrigins" (double underscore) is the Azure App Settings naming
        // convention, but it's only an INPUT syntax: .NET's standard environment-variable
        // config provider converts it to the nested path "Cors:AllowedOrigins" before it
        // ever reaches IConfiguration, so a literal flat-string lookup on the double-
        // underscore form never matches. Confirmed via a real integration test -- this
        // means the origin-matching logic here never actually ran against a real env var
        // in production; CORS worked anyway only because Azure's platform-level CORS
        // (az functionapp cors add) was doing the real enforcement.
        var corsSetting = configuration["Cors:AllowedOrigins"]
            ?? configuration["Cors__AllowedOrigins"]
            ?? configuration["CORS"];
        if (string.IsNullOrWhiteSpace(corsSetting))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return corsSetting
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddCorsHeaders(HttpResponseData response, string? origin, HashSet<string> allowedOrigins)
    {
        if (string.IsNullOrEmpty(origin) || allowedOrigins.Count == 0)
            return;

        if (allowedOrigins.Contains("*"))
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Company-Id");
            response.Headers.Add("Access-Control-Max-Age", "86400");
            return;
        }

        if (!allowedOrigins.Contains(origin))
            return;

        response.Headers.Add("Access-Control-Allow-Origin", origin);
        response.Headers.Add("Access-Control-Allow-Credentials", "true");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Company-Id");
        response.Headers.Add("Access-Control-Max-Age", "86400");
    }
}
