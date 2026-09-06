using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Humo.Core.Identity;
using Humo.Shared.Sync;

namespace Humo.Core.Sync;

/// <summary>Where the API lives, and how long we wait for it.</summary>
public sealed record SyncOptions
{
    /// <summary>Base address of the API, e.g. <c>https://humo-api.azurewebsites.net</c>.</summary>
    public string? BaseAddress { get; init; }

    /// <summary>
    /// Kept short. A phone on a marginal signal at the pit should discover it
    /// cannot sync in seconds and get on with the cook, not hold a request open.
    /// Nothing is lost by giving up: the queue is still on disk.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseAddress);
}

/// <summary>
/// The sync endpoints over HTTP.
/// <para>
/// <c>System.Net.Http</c> only — no MAUI types — so this lives in Humo.Core and
/// is testable against a stub handler rather than a device.
/// </para>
/// </summary>
public sealed class HttpSyncClient : ISyncClient
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly SyncOptions _options;

    public HttpSyncClient(HttpClient http, IAuthService auth, SyncOptions options)
    {
        _http = http;
        _auth = auth;
        _options = options;
    }

    public Task<SyncTransportResult<SyncPushResponse>> PushAsync(
        SyncPushRequest request,
        CancellationToken cancellationToken = default)
        => SendAsync<SyncPushResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, Url("sync/push"))
            {
                Content = JsonContent.Create(request),
            },
            cancellationToken);

    public Task<SyncTransportResult<SyncPullResponse>> PullAsync(
        Guid deviceId,
        long cursor,
        CancellationToken cancellationToken = default)
        => SendAsync<SyncPullResponse>(
            () => new HttpRequestMessage(HttpMethod.Get, Url($"sync/pull?deviceId={deviceId}&cursor={cursor}")),
            cancellationToken);

    /// <summary>
    /// Absolute, from the configured base address. Relative URIs would silently
    /// depend on whether whoever registered the HttpClient also set its
    /// BaseAddress, which is two places to get one thing right.
    /// </summary>
    private string Url(string path) => $"{_options.BaseAddress!.TrimEnd('/')}/{path}";

    private async Task<SyncTransportResult<T>> SendAsync<T>(
        Func<HttpRequestMessage> build,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!_options.IsConfigured)
        {
            // A checkout with no API address. Indistinguishable from offline as
            // far as the caller is concerned, and it must stay that way rather
            // than throwing on a build a contributor is running locally.
            return SyncTransportResult<T>.Failed(SyncTransportFailure.Unreachable);
        }

        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            // A guest. Not a fault: they have no account to sync to, and the
            // local database is the source of truth regardless.
            return SyncTransportResult<T>.Failed(SyncTransportFailure.Unauthorized);
        }

        using var request = build();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return SyncTransportResult<T>.Failed(SyncTransportFailure.Unreachable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout, not the caller's cancellation. Rethrowing the
            // caller's is right; swallowing it would hide a real shutdown.
            return SyncTransportResult<T>.Failed(SyncTransportFailure.Unreachable);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return SyncTransportResult<T>.Failed(SyncTransportFailure.Unauthorized);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 5xx included: the server is there but unhappy, and the queue
                // stays on disk for the next attempt either way.
                return SyncTransportResult<T>.Failed(
                    (int)response.StatusCode >= 500
                        ? SyncTransportFailure.Unreachable
                        : SyncTransportFailure.Rejected);
            }

            try
            {
                var value = await response.Content
                    .ReadFromJsonAsync<T>(cancellationToken)
                    .ConfigureAwait(false);

                return value is null
                    ? SyncTransportResult<T>.Failed(SyncTransportFailure.Rejected)
                    : SyncTransportResult<T>.Ok(value);
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or HttpRequestException)
            {
                // A captive portal answering 200 with a login page is the classic
                // case. It is not a valid response, and it is not a crash either.
                return SyncTransportResult<T>.Failed(SyncTransportFailure.Rejected);
            }
        }
    }
}
