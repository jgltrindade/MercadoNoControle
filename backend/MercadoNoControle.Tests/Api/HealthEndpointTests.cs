using System.Net;
using System.Net.Http.Json;
using MercadoNoControle.Api.DTOs;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Api;

public class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_ok_and_creates_database_through_migrations()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SystemStatusResponse>();
        Assert.NotNull(body);
        Assert.True(body.DatabaseAvailable);
        Assert.Equal("Healthy", body.Status);
        Assert.True(File.Exists(factory.DatabaseFile));
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Cors_allows_only_configured_frontend_origin()
    {
        var client = factory.CreateClient();

        var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        allowed.Headers.Add("Origin", "http://localhost:4200");
        var denied = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        denied.Headers.Add("Origin", "http://evil.example");

        var allowedResponse = await client.SendAsync(allowed);
        var deniedResponse = await client.SendAsync(denied);

        Assert.Equal("http://localhost:4200", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
