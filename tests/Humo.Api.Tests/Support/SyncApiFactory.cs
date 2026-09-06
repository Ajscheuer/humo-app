using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Humo.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Humo.Api.Tests.Support;

/// <summary>
/// The API hosted in-process, against SQLite instead of Azure SQL and a stub
/// authentication scheme instead of Entra.
/// <para>
/// The stub replaces only the <em>issuing</em> of a token, not what the API does
/// with one: the real <c>ClaimsAccountResolver</c> still maps the subject to an
/// account, so "the account comes from the token, never the body" is what these
/// tests actually exercise. Minting real Entra tokens would test Microsoft's
/// signing code, not ours.
/// </para>
/// </summary>
public sealed class SyncApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public SyncApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Out with Azure SQL. Every registration, not just the options object:
            // AddDbContext also leaves an IDbContextOptionsConfiguration behind,
            // and those accumulate rather than replace, so a second AddDbContext
            // would apply UseSqlServer and UseSqlite to the same options and EF
            // would refuse two providers at first use.
            services.Remove<DbContextOptions<HumoDbContext>>();
            services.Remove<DbContextOptions>();
            services.Remove<HumoDbContext>();

            // Matched by name because EF does not expose the interface publicly.
            services.RemoveWhere(d =>
                d.ServiceType.IsGenericType
                && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                && d.ServiceType.GenericTypeArguments.Contains(typeof(HumoDbContext)));

            services.AddDbContext<HumoDbContext>(options => options.UseSqlite(_connection));

            // Registering the scheme last makes it the default, so the JWT bearer
            // handler Program.cs wires up stands aside here.
            services
                .AddAuthentication(StubAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>(StubAuthHandler.SchemeName, _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // After the host is built, not while the collection is still being
        // configured: creating the schema from a half-configured provider would
        // run against whichever database was registered at that moment.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HumoDbContext>().Database.EnsureCreated();

        return host;
    }

    /// <summary>A client signed in as <paramref name="subject"/>.</summary>
    public HttpClient ClientFor(string subject)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(StubAuthHandler.SchemeName, subject);
        return client;
    }

    /// <summary>A client with no credentials at all.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

/// <summary>
/// Treats the Authorization header's parameter as the token subject. Anything
/// else is unauthenticated, so the "no token" case stays testable.
/// </summary>
internal sealed class StubAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Stub";

    public StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim("sub", header.Parameter)], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

internal static class ServiceCollectionExtensions
{
    /// <summary>Drops every registration of <typeparamref name="T"/>.</summary>
    public static void Remove<T>(this IServiceCollection services)
        => services.RemoveWhere(d => d.ServiceType == typeof(T));

    public static void RemoveWhere(this IServiceCollection services, Func<ServiceDescriptor, bool> match)
    {
        foreach (var descriptor in services.Where(match).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
