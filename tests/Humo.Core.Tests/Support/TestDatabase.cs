using Humo.Core.Data;
using Humo.Core.Services;
using Humo.Core.Settings;

namespace Humo.Core.Tests.Support;

/// <summary>
/// A real SQLite database in a temp file, deleted when the test finishes.
/// <para>
/// A real file rather than fake repositories on purpose: the things most likely
/// to break in this layer are round-tripping a <see cref="Guid"/>, a
/// <see cref="DateTimeOffset"/> and a nullable enum through SQLite, and a
/// substitute would exercise none of them.
/// </para>
/// </summary>
internal sealed class TestDatabase : IDatabasePath, IAsyncDisposable
{
    private readonly HumoDatabase _database;

    public TestDatabase(TestClock? clock = null)
    {
        DatabaseFilePath = Path.Combine(Path.GetTempPath(), $"humo-test-{Guid.NewGuid():N}.db3");
        _database = new HumoDatabase(this);

        Clock = clock ?? new TestClock();
        Equipment = new EquipmentRepository(_database);
        Cooks = new CookRepository(_database);
        TempEntries = new TempEntryRepository(_database);
        PitTempEntries = new PitTempEntryRepository(_database);
        FuelEvents = new FuelEventRepository(_database);
        Events = new EventRepository(_database);

        Service = new CookService(Equipment, Cooks, TempEntries, PitTempEntries, Events, Clock);
        EquipmentService = new EquipmentService(Equipment, Cooks, Clock);
        FuelService = new FuelService(FuelEvents, Equipment, Cooks, Clock);
    }

    public string DatabaseFilePath { get; }

    public TestClock Clock { get; }

    public IEquipmentRepository Equipment { get; }

    public ICookRepository Cooks { get; }

    public ITempEntryRepository TempEntries { get; }

    public IPitTempEntryRepository PitTempEntries { get; }

    public IFuelEventRepository FuelEvents { get; }

    public IEventRepository Events { get; }

    /// <summary>A CookService wired to this database and this clock.</summary>
    public ICookService Service { get; }

    public IEquipmentService EquipmentService { get; }

    public IFuelService FuelService { get; }

    /// <summary>
    /// Reads a cook back after it is over. Needs the user's settings for unit
    /// conversion, so the test supplies them.
    /// </summary>
    public ICookSummaryService SummaryServiceWith(IUserSettings settings)
        => new CookSummaryService(
            Cooks, TempEntries, PitTempEntries, FuelEvents, Events, Equipment, settings);

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();

        // Best effort: a leaked temp file is not worth failing a passing test.
        try
        {
            if (File.Exists(DatabaseFilePath))
            {
                File.Delete(DatabaseFilePath);
            }
        }
        catch (IOException)
        {
        }
    }
}
