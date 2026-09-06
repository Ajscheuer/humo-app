using System.Diagnostics;
using System.Net;
using System.Text;
using Humo.Core.Identity;
using Humo.Core.Sync;
using Humo.Shared.Sync;
using NSubstitute;

namespace Humo.Core.Tests.Sync;

public class HttpSyncClientTests
{
    private const string BaseAddress = "https://humo.example.com";

    [Fact]
    public async Task A_successful_push_returns_the_servers_answer()
    {
        var client = Build(Responds(HttpStatusCode.OK, """
            {"accepted":3,"rejected":[]}
            """));

        var result = await client.PushAsync(ABatch());

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value!.Accepted);
        Assert.False(result.Value.HasRejections);
    }

    [Fact]
    public async Task A_push_carries_the_bearer_token()
    {
        var handler = Responds(HttpStatusCode.OK, """{"accepted":0}""");

        await Build(handler).PushAsync(ABatch());

        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("a-token", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task A_pull_puts_the_device_and_cursor_in_the_query()
    {
        var handler = Responds(HttpStatusCode.OK, """{"cursor":0,"hasMore":false}""");
        var device = Guid.NewGuid();

        await Build(handler).PullAsync(device, 42);

        var url = handler.LastRequest?.RequestUri?.ToString();

        Assert.Contains($"deviceId={device}", url);
        Assert.Contains("cursor=42", url);
    }

    [Fact]
    public async Task A_build_with_no_api_address_reports_unreachable()
    {
        var client = Build(Responds(HttpStatusCode.OK, "{}"), baseAddress: null);

        var result = await client.PushAsync(ABatch());

        // A contributor's checkout with no API configured. It must degrade, not
        // throw on the first background sync.
        Assert.Equal(SyncTransportFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_guest_with_no_token_is_not_sent_anywhere()
    {
        var handler = Responds(HttpStatusCode.OK, "{}");
        var client = Build(handler, token: null);

        var result = await client.PushAsync(ABatch());

        Assert.Equal(SyncTransportFailure.Unauthorized, result.Failure);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task No_connection_reports_unreachable()
    {
        var client = Build(Throws(new HttpRequestException("no route to host")));

        var result = await client.PushAsync(ABatch());

        Assert.Equal(SyncTransportFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_request_that_outlives_the_timeout_reports_unreachable()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });

        var client = Build(handler, timeout: TimeSpan.FromMilliseconds(50));

        var result = await client.PullAsync(Guid.NewGuid(), 0);

        // A phone on a marginal signal gets on with the cook. Nothing is lost:
        // the queue is still on disk.
        Assert.Equal(SyncTransportFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_swallowed()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });

        using var cancelled = new CancellationTokenSource();
        var pending = Build(handler).PullAsync(Guid.NewGuid(), 0, cancelled.Token);

        await cancelled.CancelAsync();

        // Reporting "offline" for an app shutdown would hide a real cancellation
        // behind a retry that never comes.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_token_is_reported_as_such(HttpStatusCode status)
    {
        var result = await Build(Responds(status, "")).PushAsync(ABatch());

        Assert.Equal(SyncTransportFailure.Unauthorized, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_server_fault_is_worth_retrying(HttpStatusCode status)
    {
        var result = await Build(Responds(status, "")).PushAsync(ABatch());

        Assert.Equal(SyncTransportFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_request_the_server_refuses_is_not_retried_as_offline()
    {
        var result = await Build(Responds(HttpStatusCode.BadRequest, "")).PushAsync(ABatch());

        Assert.Equal(SyncTransportFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task A_captive_portal_answering_with_a_login_page_is_not_a_crash()
    {
        var client = Build(Responds(HttpStatusCode.OK, "<html><body>Sign in to WiFi</body></html>"));

        var result = await client.PullAsync(Guid.NewGuid(), 0);

        Assert.Equal(SyncTransportFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task An_empty_body_on_a_200_is_not_a_crash()
    {
        var result = await Build(Responds(HttpStatusCode.OK, "null")).PullAsync(Guid.NewGuid(), 0);

        Assert.Equal(SyncTransportFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task A_base_address_with_a_trailing_slash_does_not_double_it()
    {
        var handler = Responds(HttpStatusCode.OK, """{"accepted":0}""");

        await Build(handler, baseAddress: BaseAddress + "/").PushAsync(ABatch());

        Assert.Equal($"{BaseAddress}/sync/push", handler.LastRequest?.RequestUri?.ToString());
    }

    private static HttpSyncClient Build(
        StubHandler handler,
        string? baseAddress = BaseAddress,
        string? token = "a-token",
        TimeSpan? timeout = null)
    {
        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(token));

        return new HttpSyncClient(
            new HttpClient(handler),
            auth,
            new SyncOptions
            {
                BaseAddress = baseAddress,
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
            });
    }

    private static StubHandler Responds(HttpStatusCode status, string body)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    private static StubHandler Throws(Exception exception)
        => new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    private static SyncPushRequest ABatch() => new() { DeviceId = Guid.NewGuid() };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _respond(request, cancellationToken);
        }
    }
}
