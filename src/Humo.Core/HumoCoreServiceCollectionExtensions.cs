using Humo.Core.Analytics;
using Humo.Core.Data;
using Humo.Core.Entitlements;
using Humo.Core.Identity;
using Humo.Core.Localization;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Sync;
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

        // One shared account context: sign-in changes what the whole app is
        // looking at, and every repository must see that at once.
        services.AddSingleton<IAccountContext, AccountContext>();
        services.AddSingleton<IRecordOwnership, RecordOwnership>();
        services.AddSingleton<IAccountService, AccountService>();
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
        services.AddSingleton<IFuelEventRepository, FuelEventRepository>();
        services.AddSingleton<IEventRepository, EventRepository>();

        // Sync. The transport itself is registered by the app, which is where
        // the API address and the HttpClient come from; everything above it is
        // plain logic and belongs here.
        services.AddSingleton<ISyncQueue, SyncQueue>();
        services.AddSingleton<ISyncState, SyncState>();
        services.AddSingleton<ISyncService, Sync.SyncService>();
        services.AddSingleton<ISyncFailureLog, SyncFailureLog>();
        services.AddSingleton<ISyncTrigger, SyncTrigger>();

        // Entitlements. The store itself is registered by the app, which is
        // where the billing client lives; the cache and the rules are plain
        // logic and belong here.
        services.AddSingleton<IClientEntitlementService, ClientEntitlementService>();

        // The store in a build that has none. Registered here so a checkout with
        // no store keys runs and shows an honest paywall; the app registers a
        // real one after this, and the later registration wins.
        services.AddSingleton<IPurchaseService, UnavailablePurchaseService>();

        services.AddSingleton<ICookService, CookService>();
        services.AddSingleton<IEquipmentService, EquipmentService>();
        services.AddSingleton<IFuelService, FuelService>();
        services.AddSingleton<ICookSummaryService, CookSummaryService>();

        services.AddTransient<AppSettingsViewModel>();
        services.AddTransient<StartCookViewModel>();
        services.AddTransient<ActiveCookViewModel>();
        services.AddTransient<EquipmentListViewModel>();
        services.AddTransient<EquipmentEditViewModel>();
        services.AddTransient<FuelSheetViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<CookHistoryViewModel>();
        services.AddTransient<CookSummaryViewModel>();
        services.AddTransient<PaywallViewModel>();
        services.AddTransient<CookInsightsViewModel>();

        return services;
    }
}
