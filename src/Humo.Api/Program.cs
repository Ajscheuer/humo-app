// Humo API — ASP.NET Core Minimal API.
//
// Endpoints are registered per feature area in their own files as they arrive
// (sync in slice 5, entitlements in slice 6, analytics in slice 7). Program.cs
// stays a wiring file and does not accumulate endpoint bodies.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness probe for App Service. Deliberately unauthenticated and free of any
// database call, so it reports whether the process is up rather than whether
// Azure SQL has finished resuming from auto-pause.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>
/// Exposed so Humo.Api.Tests can host the API in-process with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
