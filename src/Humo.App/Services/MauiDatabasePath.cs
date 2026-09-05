using Humo.Core.Data;

namespace Humo.App.Services;

/// <summary>
/// Where the SQLite file lives on a device.
/// <para>
/// <c>AppDataDirectory</c> and not the cache directory: SQLite is the source of
/// truth during a cook, and the OS may clear the cache directory under storage
/// pressure — which is exactly what an overnight brisket is.
/// </para>
/// </summary>
public sealed class MauiDatabasePath : IDatabasePath
{
    public const string FileName = "humo.db3";

    public string DatabaseFilePath { get; } =
        Path.Combine(FileSystem.AppDataDirectory, FileName);
}
