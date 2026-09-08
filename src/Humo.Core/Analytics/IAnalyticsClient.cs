using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Humo.Core.Identity;
using Humo.Core.Sync;
using Humo.Shared.Analytics;

namespace Humo.Core.Analytics;

/// <summary>Why the server did not hand back a cook's analytics.</summary>
public enum AnalyticsUnavailable
{
    /// <summary>It did. <see cref="AnalyticsFetch.Value"/> is set.</summary>
    None = 0,

    /// <summary>No connection, or no API configured in this build.</summary>
    Offline = 1,

    /// <summary>A free account. The screen offers the upgrade rather than an error.</summary>
    NeedsPro = 2,

    /// <summary>
    /// The server has nothing for this cook: it has never synced, or it is still
    /// running. Not an error and not a paywall.
    /// </summary>
    NotComputed = 3,
}

public sealed record AnalyticsFetch
{
    public CookAnalytics? Value { get; init; }

    public AnalyticsUnavailable Reason { get; init; }

    public bool Succeeded => Value is not null;

    public static AnalyticsFetch Ok(CookAnalytics value) => new() { Value = value };

    public static AnalyticsFetch Unavailable(AnalyticsUnavailable reason) => new() { Reason = reason };
}

/// <summary>
/// Reads a cook's server-computed analytics.
/// <para>
/// The four ways this can not work are four different screens — offline, needs
/// Pro, nothing computed yet, or a number — so they are four return values
/// rather than one exception a caller has to interpret.
/// </para>
/// </summary>
public interface IAnalyticsClient
{
    Task<AnalyticsFetch> GetAsync(Guid cookId, CancellationToken cancellationToken = default);
}

internal sealed class HttpAnalyticsClient : IAnalyticsClient
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly SyncOptions _options;

    public HttpAnalyticsClient(HttpClient http, IAuthService auth, SyncOptions options)
    {
        _http = http;
        _auth = auth;
        _options = options;
    }

    public async Task<AnalyticsFetch> GetAsync(Guid cookId, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline);
        }

        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token))
        {
            // A guest. They have no account, so they have no server-side
            // analytics, and the upgrade path is the honest thing to show.
            return AnalyticsFetch.Unavailable(AnalyticsUnavailable.NeedsPro);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.BaseAddress!.TrimEnd('/')}/analytics/cooks/{cookId}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                return AnalyticsFetch.Unavailable(AnalyticsUnavailable.NeedsPro);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return AnalyticsFetch.Unavailable(AnalyticsUnavailable.NotComputed);
            }

            if (!response.IsSuccessStatusCode)
            {
                return AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline);
            }

            var value = await response.Content
                .ReadFromJsonAsync<CookAnalytics>(cancellationToken)
                .ConfigureAwait(false);

            return value is null
                ? AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline)
                : AnalyticsFetch.Ok(value);
        }
        catch (HttpRequestException)
        {
            return AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline);
        }
        catch (System.Text.Json.JsonException)
        {
            return AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline);
        }
    }
}
