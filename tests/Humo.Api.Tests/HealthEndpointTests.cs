using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Humo.Api.Tests;

/// <summary>
/// Proves the API hosts in-process under <see cref="WebApplicationFactory{T}"/>,
/// which is the harness every later endpoint test (sync, entitlements,
/// analytics) will be written against.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_reports_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_returns_JSON()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Health_needs_no_authentication()
    {
        // App Service probes it with no token. If auth is ever applied globally,
        // this endpoint has to stay exempt or the app looks dead to Azure.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_route_is_a_404_rather_than_a_500()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_does_not_answer_a_POST()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/health", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
