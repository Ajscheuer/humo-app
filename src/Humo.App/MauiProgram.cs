using System.Globalization;
using Humo.App.Services;
using Humo.App.Views;
using Humo.Core;
using Humo.Core.Data;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Identity;
using Humo.Core.Settings;
using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.Logging;

namespace Humo.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()

            // Registers the chart handlers (and SkiaSharp underneath them).
            // Without this the CartesianChart on the cook summary has no handler
            // and the page fails at runtime -- a device-only failure that
            // compiles perfectly well, which is why it is called out here.
            .UseLiveCharts();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RegisterServices(builder.Services);
        RegisterViews(builder.Services);
        RegisterRoutes();

        var app = builder.Build();

        ServiceHelper.Initialize(app.Services);
        ApplyStartupCulture(app.Services);
        ResolveAccount(app.Services);

        return app;
    }

    /// <summary>
    /// Routes that are navigated to but are not tabs. Registered here with the
    /// rest of the app wiring rather than in a page constructor, which
    /// CLAUDE.md keeps to <c>InitializeComponent()</c>. The tab routes come from
    /// AppShell.xaml.
    /// </summary>
    private static void RegisterRoutes()
    {
        Routing.RegisterRoute(AppRoutes.StartCook, typeof(StartCookPage));
        Routing.RegisterRoute(AppRoutes.EditEquipment, typeof(EquipmentEditPage));
        Routing.RegisterRoute(AppRoutes.FuelSheet, typeof(FuelSheetPage));
        Routing.RegisterRoute(AppRoutes.CookSummary, typeof(CookSummaryPage));
        Routing.RegisterRoute(AppRoutes.SignIn, typeof(SignInPage));
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Platform capabilities are registered here as implementations of
        // interfaces declared in Humo.Core, so nothing in Humo.Core ever
        // references MAUI.
        services.AddSingleton<IAppPreferences, MauiAppPreferences>();
        services.AddSingleton<IDatabasePath, MauiDatabasePath>();
        services.AddSingleton<INavigationService, ShellNavigationService>();

        // Tenant configuration. Absent in a checkout with no Entra tenant, which
        // is a normal state: IAuthService reports it and the sign-in screen says
        // so, while "continue without an account" keeps working.
        services.AddSingleton(AuthConfiguration.Load());
        services.AddSingleton<IAuthService, EntraAuthService>();

        // Everything else -- services, repositories, ViewModels -- comes from
        // Humo.Core, which registers the same graph a test builds.
        services.AddHumoCore();
    }

    private static void RegisterViews(IServiceCollection services)
    {
        services.AddSingleton<AppShell>();
        services.AddTransient<MainPage>();
        services.AddTransient<StartCookPage>();
        services.AddTransient<ActiveCookPage>();
        services.AddTransient<EquipmentListPage>();
        services.AddTransient<EquipmentEditPage>();
        services.AddTransient<FuelSheetPage>();
        services.AddTransient<SignInPage>();
        services.AddTransient<CookHistoryPage>();
        services.AddTransient<CookSummaryPage>();
    }

    /// <summary>
    /// Establishes whose data this launch is looking at, before anything reads
    /// the database.
    /// <para>
    /// Blocking on purpose. Every repository scopes its queries to the current
    /// account, so a screen that loaded before this resolved would show an empty
    /// app and then quietly write records under the wrong owner.
    /// </para>
    /// </summary>
    private static void ResolveAccount(IServiceProvider services)
        => services.GetRequiredService<IAccountService>()
            .InitializeAsync()
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Applies the culture resolution chain at startup: in-app override →
    /// device language → English.
    /// </summary>
    private static void ApplyStartupCulture(IServiceProvider services)
    {
        var settings = services.GetRequiredService<IUserSettings>();
        var localizer = services.GetRequiredService<ILocalizer>();

        localizer.SetCulture(settings.LanguageOverride ?? CultureInfo.CurrentUICulture);
    }
}
