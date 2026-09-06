using System.Text.Json;
using Humo.Core.Identity;
using Humo.Core.Sync;

namespace Humo.App.Services;

/// <summary>
/// Where the per-environment values come from: the identity tenant, and the
/// address of the API.
/// <para>
/// An embedded <c>authsettings.json</c> first, then environment variables.
/// The file is what a shipped build reads: an app launched from the home screen
/// inherits no environment, so environment variables alone would mean
/// <see cref="AuthOptions.IsConfigured"/> could never be true on a real device.
/// The variables remain for desktop <c>dotnet run</c>, where they are quicker
/// than editing a file.
/// </para>
/// <para>
/// None of these are secrets — a public mobile client has no client secret — but
/// they are per-environment, so the file is git-ignored and absent by default.
/// A checkout without one builds, runs, and logs cooks as a guest.
/// </para>
/// </summary>
public static class AppConfiguration
{
    // Still "authsettings" even though it now also carries the API address:
    // a configured build already ships a file by that name, and renaming it
    // would silently un-configure every existing checkout and pipeline.
    internal const string SettingsFileName = "authsettings.json";

    internal const string ClientIdVariable = "HUMO_ENTRA_CLIENT_ID";
    internal const string AuthorityVariable = "HUMO_ENTRA_AUTHORITY";
    internal const string RedirectUriVariable = "HUMO_ENTRA_REDIRECT_URI";
    internal const string ScopesVariable = "HUMO_ENTRA_SCOPES";
    internal const string ApiBaseAddressVariable = "HUMO_API_BASE_ADDRESS";

    public static AuthOptions LoadAuth() => LoadFromFile() ?? LoadFromEnvironment();

    /// <summary>
    /// Where the API lives, from the same file and the same fallback. A build
    /// with none configured runs entirely local: sync reports itself unreachable
    /// and the cook is unaffected, because SQLite is the source of truth anyway.
    /// </summary>
    public static SyncOptions LoadSync() => new()
    {
        BaseAddress = Blank(ReadFile()?.ApiBaseAddress)
                      ?? Blank(Environment.GetEnvironmentVariable(ApiBaseAddressVariable)),
    };

    /// <summary>
    /// The bundled settings file, or null when this build has none — the normal
    /// state for a fresh checkout, and not an error.
    /// </summary>
    private static AuthOptions? LoadFromFile()
    {
        var file = ReadFile();

        return file is null
            ? null
            : new AuthOptions
            {
                ClientId = Blank(file.ClientId),
                Authority = Blank(file.Authority),
                RedirectUri = Blank(file.RedirectUri),
                Scopes = file.Scopes ?? [],
            };
    }

    private static SettingsFile? ReadFile()
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(SettingsFileName)
                .GetAwaiter()
                .GetResult();

            return JsonSerializer.Deserialize<SettingsFile>(stream, FileFormat);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            // A malformed file is worth failing loudly over: it means someone
            // configured this build and got it wrong, which is far more
            // confusing to debug as a silent fallback to "nothing available".
            throw;
        }
    }

    /// <summary>
    /// Case-insensitive so a file written with camelCase keys — the obvious way
    /// to write JSON — configures the build instead of deserializing to all
    /// nulls and reporting itself as "no tenant configured".
    /// </summary>
    private static readonly JsonSerializerOptions FileFormat = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static AuthOptions LoadFromEnvironment() => new()
    {
        ClientId = Blank(Environment.GetEnvironmentVariable(ClientIdVariable)),
        Authority = Blank(Environment.GetEnvironmentVariable(AuthorityVariable)),
        RedirectUri = Blank(Environment.GetEnvironmentVariable(RedirectUriVariable)),
        Scopes = Environment.GetEnvironmentVariable(ScopesVariable)
            ?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [],
    };

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SettingsFile
    {
        public string? ClientId { get; init; }

        public string? Authority { get; init; }

        public string? RedirectUri { get; init; }

        public string[]? Scopes { get; init; }

        public string? ApiBaseAddress { get; init; }
    }
}
