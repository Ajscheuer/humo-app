using Humo.Core.Data;
using Humo.Core.Localization;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Time;
using Humo.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Humo.Core;

/// <summary>
/// Registers everything in Humo.Core.
/// <para>
/// Keeping this here rather than in MauiProgram means the same graph can be
/// built in a test, which is what makes the ViewModels testable without a
/// device. The app still supplies the two platform pieces: where the database
/// file lives, and how preferences are stored.
/// </para>
/// </summary>
public static class HumoCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHumoCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ILocalizer, Localizer>();
        services.AddSingleton<IUserSettings, UserSettings>();

        // One database, one connection, shared by every repository.
        services.AddSingleton<HumoDatabase>();
        services.AddSingleton<IHumoDatabase>(sp => sp.GetRequiredService<HumoDatabase>());
        services.AddSingleton<IConnectionSource>(sp => sp.GetRequiredService<HumoDatabase>());

        services.AddSingleton<IEquipmentRepository, EquipmentRepository>();
        services.AddSingleton<ICookRepository, CookRepository>();
        services.AddSingleton<ITempEntryRepository, TempEntryRepository>();
        services.AddSingleton<IPitTempEntryRepository, PitTempEntryRepository>();

        services.AddSingleton<ICookService, CookService>();

        services.AddTransient<AppSettingsViewModel>();
        services.AddTransient<StartCookViewModel>();
        services.AddTransient<ActiveCookViewModel>();

        return services;
    }
}
