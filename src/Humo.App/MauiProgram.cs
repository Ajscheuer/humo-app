using System.Globalization;
using Humo.App.Services;
using Humo.App.Views;
using Humo.Core.Localization;
using Humo.Core.Settings;
using Humo.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace Humo.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RegisterServices(builder.Services);
        RegisterViews(builder.Services);

        var app = builder.Build();

        ServiceHelper.Initialize(app.Services);
        ApplyStartupCulture(app.Services);

        return app;
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Platform capabilities are registered here as implementations of
        // interfaces declared in Humo.Core, so nothing in Humo.Core ever
        // references MAUI.
        services.AddSingleton<IAppPreferences, MauiAppPreferences>();
        services.AddSingleton<IUserSettings, UserSettings>();
        services.AddSingleton<ILocalizer, Localizer>();
    }

    private static void RegisterViews(IServiceCollection services)
    {
        services.AddTransient<AppSettingsViewModel>();
        services.AddTransient<MainPage>();
    }

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
