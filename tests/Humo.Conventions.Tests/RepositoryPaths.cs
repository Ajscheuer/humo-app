namespace Humo.Conventions.Tests;

/// <summary>
/// Locates the repository on disk. Convention tests inspect source files rather
/// than only compiled output, so they need to find the tree they were built
/// from.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>The repository root — the directory containing Humo.sln.</summary>
    public static string Root { get; } = FindRoot();

    public static string Source(params string[] segments)
        => Path.Combine([Root, "src", .. segments]);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Humo.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root (no Humo.sln found).");
    }
}
