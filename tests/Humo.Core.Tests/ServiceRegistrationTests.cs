using Humo.Core;
using Humo.Core.Data;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Humo.Core.Tests;

/// <summary>
/// A missing DI registration compiles fine and fails on first launch, on a
/// device, in front of whoever opened the app. These tests build the real
/// container instead.
/// </summary>
public class ServiceRegistrationTests
{
    /// <summary>
    /// The container as the app builds it: Humo.Core's own graph plus the three
    /// things only the platform can supply.
    /// </summary>
    private static ServiceProvider BuildAppContainer()
        => new ServiceCollection()
            .AddSingleton<IAppPreferences>(Substitute.For<IAppPreferences>())
            .AddSingleton<IDatabasePath>(new TestDatabasePath())
            .AddSingleton<INavigationService>(Substitute.For<INavigationService>())
            .AddHumoCore()
            .BuildServiceProvider(validateScopes: true);

    [Theory]
    [InlineData(typeof(StartCookViewModel))]
    [InlineData(typeof(ActiveCookViewModel))]
    [InlineData(typeof(AppSettingsViewModel))]
    [InlineData(typeof(EquipmentListViewModel))]
    [InlineData(typeof(EquipmentEditViewModel))]
    [InlineData(typeof(FuelSheetViewModel))]
    public void Every_view_model_can_be_resolved(Type viewModelType)
    {
        using var provider = BuildAppContainer();

        Assert.NotNull(provider.GetRequiredService(viewModelType));
    }

    [Fact]
    public void The_cook_service_and_its_repositories_resolve()
    {
        using var provider = BuildAppContainer();

        Assert.NotNull(provider.GetRequiredService<ICookService>());
        Assert.NotNull(provider.GetRequiredService<IEquipmentService>());
        Assert.NotNull(provider.GetRequiredService<IFuelService>());
        Assert.NotNull(provider.GetRequiredService<IEquipmentRepository>());
        Assert.NotNull(provider.GetRequiredService<ICookRepository>());
        Assert.NotNull(provider.GetRequiredService<ITempEntryRepository>());
        Assert.NotNull(provider.GetRequiredService<IPitTempEntryRepository>());
        Assert.NotNull(provider.GetRequiredService<IFuelEventRepository>());
        Assert.NotNull(provider.GetRequiredService<IEventRepository>());
    }

    [Fact]
    public void Every_repository_shares_one_database()
    {
        using var provider = BuildAppContainer();

        // Two connections to the same SQLite file is how a cook's reading gets
        // written into one and read back as missing from the other.
        Assert.Same(
            provider.GetRequiredService<IHumoDatabase>(),
            provider.GetRequiredService<IConnectionSource>());
    }

    [Fact]
    public void The_container_can_be_torn_down_synchronously()
    {
        var provider = BuildAppContainer();
        _ = provider.GetRequiredService<ICookService>();

        // A singleton that is IAsyncDisposable only makes the container throw on
        // synchronous disposal, which turns app shutdown into a crash.
        provider.Dispose();
    }

    [Fact]
    public async Task Closing_the_database_twice_is_harmless()
    {
        var database = new HumoDatabase(new TestDatabasePath());
        await database.InitializeAsync();

        await database.DisposeAsync();
        database.Dispose();
    }

    /// <summary>A path in the temp directory; nothing here opens the file.</summary>
    private sealed class TestDatabasePath : IDatabasePath
    {
        public string DatabaseFilePath { get; } =
            Path.Combine(Path.GetTempPath(), $"humo-di-{Guid.NewGuid():N}.db3");
    }
}
