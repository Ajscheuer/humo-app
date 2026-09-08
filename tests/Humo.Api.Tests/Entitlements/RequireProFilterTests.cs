using System.Net;
using System.Security.Claims;
using Humo.Api.Auth;
using Humo.Api.Entitlements;
using Humo.Api.Tests.Support;
using Microsoft.AspNetCore.Http;

namespace Humo.Api.Tests.Entitlements;

/// <summary>
/// The gate slice 7's analytics endpoints sit behind. Tested here, with the
/// entitlement it reads, so the first endpoint to use it inherits something
/// already proven rather than inventing its own check.
/// </summary>
public class RequireProFilterTests : IAsyncLifetime
{
    private readonly EntitlementTestContext _ctx = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task A_pro_account_reaches_the_endpoint()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase());

        var reached = false;
        var result = await InvokeAsync(SignedIn(), () => reached = true);

        Assert.True(reached);
        Assert.Equal("the endpoint ran", result);
    }

    [Fact]
    public async Task A_free_account_is_refused_and_the_endpoint_never_runs()
    {
        var reached = false;
        var result = await InvokeAsync(SignedIn(), () => reached = true);

        Assert.False(reached);
        Assert.Equal(HttpStatusCode.PaymentRequired, StatusOf(result));
    }

    [Fact]
    public async Task An_expired_subscription_is_refused()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase(expiresAt: _ctx.Now.AddDays(30)));
        _ctx.Time.Advance(TimeSpan.FromDays(31));

        var result = await InvokeAsync(SignedIn(), () => { });

        // The client may still be showing its cached "Pro" badge. The server is
        // where that stops mattering.
        Assert.Equal(HttpStatusCode.PaymentRequired, StatusOf(result));
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_a_401_not_a_402()
    {
        var result = await InvokeAsync(new ClaimsPrincipal(new ClaimsIdentity()), () => { });

        // Different remedies: signing in fixes one, and only buying fixes the
        // other. A 402 here would send a signed-out user to a paywall.
        Assert.Equal(HttpStatusCode.Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task A_client_claiming_to_be_pro_in_a_header_is_still_refused()
    {
        var http = new DefaultHttpContext { User = SignedIn() };
        http.Request.Headers["X-Entitlement"] = "pro";
        http.Request.Headers["X-Pro"] = "true";

        var result = await InvokeAsync(http, () => { });

        // Nothing in a request grants anything. The row does.
        Assert.Equal(HttpStatusCode.PaymentRequired, StatusOf(result));
    }

    private ClaimsPrincipal SignedIn()
        => new(new ClaimsIdentity([new Claim("sub", _ctx.Account.ToString())], "Test"));

    private Task<object?> InvokeAsync(ClaimsPrincipal user, Action onReached)
        => InvokeAsync(new DefaultHttpContext { User = user }, onReached);

    private async Task<object?> InvokeAsync(HttpContext http, Action onReached)
    {
        var filter = new RequireProFilter(new ClaimsAccountResolver(), _ctx.Service);
        var context = EndpointFilterInvocationContext.Create(http);

        return await filter.InvokeAsync(context, _ =>
        {
            onReached();
            return ValueTask.FromResult<object?>("the endpoint ran");
        });
    }

    /// <summary>The status an <see cref="IResult"/> would write.</summary>
    private static HttpStatusCode StatusOf(object? result)
        => result is IStatusCodeHttpResult { StatusCode: { } code }
            ? (HttpStatusCode)code
            : throw new InvalidOperationException($"Not a status result: {result?.GetType().Name ?? "null"}");
}
