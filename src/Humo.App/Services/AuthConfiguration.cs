using System.Text.Json;
using Humo.Core.Identity;

namespace Humo.App.Services;

/// <summary>
/// Where the tenant values come from.
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
public static class AuthConfiguration
{
    internal const string SettingsFileName = "authsettings.json";

    internal const string ClientIdVariable = "HUMO_ENTRA_CLIENT_ID";
    internal const string AuthorityVariable = "HUMO_ENTRA_AUTHORITY";
    internal const string RedirectUriVariable = "HUMO_ENTRA_REDIRECT_URI";
    internal const string ScopesVariable = "HUMO_ENTRA_SCOPES";

    public static AuthOptions Load() => LoadFromFile() ?? LoadFromEnvironment();

    /// <summary>
    /// The bundled settings file, or null when this build has none — the normal
    /// state for a fresh checkout, and not an error.
    /// </summary>
    private static AuthOptions? LoadFromFile()
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(SettingsFileName)
                .GetAwaiter()
                .GetResult();

            var file = JsonSerializer.Deserialize<AuthSettingsFile>(stream);

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
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            // A malformed file is worth failing loudly over -- it means someone
            // configured a tenant and got it wrong, which is far more confusing
            // to debug as a silent fallback to "no sign-in available".
            throw;
        }
    }

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

    private sealed record AuthSettingsFile
    {
        public string? ClientId { get; init; }

        public string? Authority { get; init; }

        public string? RedirectUri { get; init; }

        public string[]? Scopes { get; init; }
    }
}
