using System.Xml.Linq;
using Humo.Core.Localization;

namespace Humo.Conventions.Tests;

/// <summary>
/// Guards the project boundaries the architecture depends on. Both rules below
/// are easy to break with an innocent-looking `using`, and neither breaks
/// anything visibly until much later — when a ViewModel suddenly needs a device
/// to test, or the API and app disagree about the wire contract.
/// </summary>
public class ProjectBoundaryTests
{
    [Fact]
    public void No_ViewModel_uses_ConfigureAwait_false()
    {
        // ViewModels mutate bound ObservableCollections and observable
        // properties. Resuming off the UI thread to do that throws on iOS and
        // Android -- a crash that no test on this machine can see, because there
        // is no UI thread here to leave. Commands start on the UI thread, so
        // awaiting without ConfigureAwait resumes there.
        //
        // Services and repositories still use it; nothing below the ViewModel
        // touches the UI.
        var offending = Directory
            .EnumerateFiles(
                RepositoryPaths.Source("Humo.Core", "ViewModels"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "ConfigureAwait(false)", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order()
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"These ViewModels use ConfigureAwait(false): {string.Join(", ", offending)}. "
            + "Remove it -- see the MVVM rules in CLAUDE.md.");
    }

    [Fact]
    public void Humo_Core_does_not_reference_MAUI()
    {
        // This is what keeps every ViewModel and service unit-testable with no
        // workload and no device. Platform capabilities belong behind an
        // interface declared in Humo.Core and implemented in Humo.App.
        var offending = typeof(Localizer).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.Maui", StringComparison.OrdinalIgnoreCase))
            .Order()
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"Humo.Core references MAUI assemblies: {string.Join(", ", offending)}. "
            + "Declare an interface in Humo.Core and implement it in Humo.App instead.");
    }

    [Fact]
    public void Humo_Shared_references_nothing()
    {
        // Humo.Shared is the wire contract shared by app and API. Keeping it
        // free of dependencies is what stops it drifting toward either side.
        var project = XDocument.Load(RepositoryPaths.Source("Humo.Shared", "Humo.Shared.csproj"));

        var references = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? "(unnamed)")
            .Order()
            .ToList();

        Assert.True(
            references.Count == 0,
            $"Humo.Shared has references it should not have: {string.Join(", ", references)}.");
    }

    [Fact]
    public void Humo_Core_does_not_reference_the_app_or_the_API()
    {
        var offending = typeof(Localizer).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .Where(name => name is "Humo.App" or "Humo.Api")
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"Humo.Core references {string.Join(", ", offending)}. References flow "
            + "Humo.Shared -> Humo.Core -> Humo.App, never back.");
    }
}
