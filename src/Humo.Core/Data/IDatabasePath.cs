namespace Humo.Core.Data;

/// <summary>
/// Where the SQLite file lives. Implemented in Humo.App over the platform's
/// app-data directory; tests point it at a temporary file.
/// </summary>
public interface IDatabasePath
{
    string DatabaseFilePath { get; }
}
