using Humo.Api.Auth;
using Humo.Api.Data;
using Humo.Api.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

// Humo API — ASP.NET Core Minimal API.
//
// Endpoints are registered per feature area in their own files as they arrive
// (sync in slice 5, entitlements in slice 6, analytics in slice 7). Program.cs
// stays a wiring file and does not accumulate endpoint bodies.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountResolver, ClaimsAccountResolver>();
builder.Services.AddScoped<ISyncService, SyncService>();

// Azure SQL. The connection string comes from configuration, which in App
// Service means a managed-identity connection rather than a secret in a file.
builder.Services.AddDbContext<HumoDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("HumoDatabase")));

// Bearer tokens from Entra External ID. The authority is configuration because
// it is per-tenant; a build with none configured still starts, and every
// authorized endpoint simply refuses, which is the honest behaviour for an API
// with no identity provider behind it.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Liveness probe for App Service. Deliberately unauthenticated and free of any
// database call, so it reports whether the process is up rather than whether
// Azure SQL has finished resuming from auto-pause.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapSyncEndpoints();

app.Run();

/// <summary>
/// Exposed so Humo.Api.Tests can host the API in-process with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
