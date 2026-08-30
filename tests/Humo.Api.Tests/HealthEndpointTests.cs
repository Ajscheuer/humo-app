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
}
