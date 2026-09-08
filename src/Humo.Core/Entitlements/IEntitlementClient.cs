using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Humo.Core.Identity;
using Humo.Core.Sync;
using Humo.Shared.Entitlements;

namespace Humo.Core.Entitlements;

/// <summary>
/// Fetches the entitlement from the API.
/// <para>
/// Separate from <see cref="IClientEntitlementService"/> so the caching and the
/// "unknown is not Pro" rules are testable without a server, and so an
/// unreachable API is a null rather than an exception on a background path.
/// </para>
/// </summary>
public interface IEntitlementClient
{
    /// <summary>The server's answer, or null when it could not be obtained.</summary>
    Task<EntitlementState?> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class HttpEntitlementClient : IEntitlementClient
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly SyncOptions _options;

    public HttpEntitlementClient(HttpClient http, IAuthService auth, SyncOptions options)
    {
        _http = http;
        _auth = auth;
        _options = options;
    }

    public async Task<EntitlementState?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return null;
        }

        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.BaseAddress!.TrimEnd('/')}/entitlements/me");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

            return response.StatusCode is HttpStatusCode.OK
                ? await response.Content
                    .ReadFromJsonAsync<EntitlementState>(cancellationToken)
                    .ConfigureAwait(false)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our timeout, not the caller's. Rethrowing the caller's is right;
            // swallowing it would hide a real shutdown.
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            // A captive portal answering 200 with a login page.
            return null;
        }
    }
}
