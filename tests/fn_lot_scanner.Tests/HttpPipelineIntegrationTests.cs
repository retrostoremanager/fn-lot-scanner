using System.Net;
using FluentAssertions;

namespace fn_lot_scanner.Tests;

// Real end-to-end tests of the running Functions host (routing + CorsMiddleware +
// JwtAuthenticationMiddleware together, via FuncHostFixture) -- the actual layer where two
// real bugs lived: CORS preflight 404ing before any middleware ran, and a Core Tools/.NET 10
// host assembly mismatch that crashed the app outright. Unit tests on function bodies
// directly can't catch either one, since they bypass routing and the middleware pipeline
// entirely.
[Collection(nameof(FuncHostCollection))]
public class HttpPipelineIntegrationTests(FuncHostFixture fixture)
{
    [Theory]
    [InlineData("/api/lot-scans")]
    [InlineData("/api/lot-scans/00000000-0000-0000-0000-000000000000")]
    public async Task Request_without_a_bearer_token_is_rejected_before_reaching_the_function_body(string path)
    {
        var response = await fixture.Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Options_preflight_from_an_allowed_origin_succeeds_with_cors_headers()
    {
        // This is the actual regression: before the fix, this request 404'd (no function
        // matched OPTIONS) instead of ever reaching CorsMiddleware.
        var request = new HttpRequestMessage(HttpMethod.Options, "api/lot-scans");
        request.Headers.Add("Origin", "http://localhost:8081");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("http://localhost:8081");
    }

    [Fact]
    public async Task Options_preflight_from_a_disallowed_origin_gets_no_cors_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "api/lot-scans");
        request.Headers.Add("Origin", "http://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Request_with_a_malformed_bearer_token_is_rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/lot-scans");
        request.Headers.Add("Authorization", "Bearer not-a-real-jwt");

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_real_get_request_is_also_answered_with_cors_headers_not_just_preflight()
    {
        // CorsMiddleware's job isn't done at OPTIONS -- the actual response (here, the 401
        // from JwtAuthenticationMiddleware) needs the header too, or the browser hides the
        // response from the calling JS even though the server answered.
        var request = new HttpRequestMessage(HttpMethod.Get, "api/lot-scans");
        request.Headers.Add("Origin", "http://localhost:8081");

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("http://localhost:8081");
    }
}

[CollectionDefinition(nameof(FuncHostCollection))]
public class FuncHostCollection : ICollectionFixture<FuncHostFixture>;
