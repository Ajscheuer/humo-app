using Humo.Core.Data;
using Humo.Core.Services;

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

        Service = new CookService(Equipment, Cooks, TempEntries, PitTempEntries, Clock);
    }

    public string DatabaseFilePath { get; }

    public TestClock Clock { get; }

    public IEquipmentRepository Equipment { get; }

    public ICookRepository Cooks { get; }

    public ITempEntryRepository TempEntries { get; }

    public IPitTempEntryRepository PitTempEntries { get; }

    /// <summary>A CookService wired to this database and this clock.</summary>
    public ICookService Service { get; }

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
