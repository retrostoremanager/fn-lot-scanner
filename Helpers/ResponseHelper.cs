using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker.Http;

namespace fn_lot_scanner.Helpers;

// Mirrors api-gamedb's Helpers/ResponseHelper.cs for consistency across RSM services.
public static class ResponseHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public static async Task<HttpResponseData> Ok<T>(HttpRequestData req, T body)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }

    public static async Task<HttpResponseData> Accepted<T>(HttpRequestData req, T body)
    {
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }

    public static async Task<HttpResponseData> BadRequest(HttpRequestData req, string? message = null)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message ?? "Bad request" }, JsonOptions));
        return response;
    }

    public static Task<HttpResponseData> NotFound(HttpRequestData req) =>
        Task.FromResult(req.CreateResponse(HttpStatusCode.NotFound));

    public static async Task<HttpResponseData> Conflict(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.Conflict);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message }, JsonOptions));
        return response;
    }
}
