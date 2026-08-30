using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Humo.Conventions.Tests;

/// <summary>
/// Enforces "all user-facing strings go through .resx" on the XAML side.
/// <para>
/// The resource parity tests catch a string added to English but not Spanish;
/// this catches the case they cannot see — a string that never reached a
/// resource file at all, and so is permanently English in both languages.
/// </para>
/// </summary>
public partial class HardcodedStringTests
{
    /// <summary>
    /// Attributes that put text in front of a user. A value here must be a
    /// binding or markup extension, not a literal.
    /// </summary>
    private static readonly string[] UserFacingAttributes =
    [
        "Text",
        "Title",
        "Placeholder",
        "Description",
        "Hint",
        "ToolTipProperties.Text",
        "SemanticProperties.Description",
        "SemanticProperties.Hint",
    ];

    [Fact]
    public void No_XAML_file_contains_a_hardcoded_user_facing_string()
    {
        var violations = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (var (attribute, value) in UserFacingAttributeValues(file))
            {
                if (IsResolvedAtRuntime(value) || value.Length == 0)
                {
                    continue;
                }

                violations.Add(
                    $"{Path.GetRelativePath(RepositoryPaths.Root, file)}: {attribute}=\"{value}\"");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hardcoded user-facing strings in XAML. Move them to AppResources.resx and "
            + $"AppResources.es.resx, then bind with {{loc:Translate Key}}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Order()));
    }

    [Fact]
    public void The_guard_itself_recognises_a_hardcoded_string()
    {
        // A guard that cannot fail is not a guard. This pins the detection rule
        // so a future refactor of the matching logic cannot quietly neuter it.
        Assert.False(IsResolvedAtRuntime("Start cook"));
        Assert.True(IsResolvedAtRuntime("{loc:Translate Cook_Start}"));
        Assert.True(IsResolvedAtRuntime("{Binding Title}"));
        Assert.True(IsResolvedAtRuntime("{StaticResource Something}"));
    }

    /// <summary>
    /// True when the value is a binding or markup extension, so its text is
    /// resolved at runtime rather than baked into the layout.
    /// </summary>
    private static bool IsResolvedAtRuntime(string value)
        => value.TrimStart().StartsWith('{');

    private static IEnumerable<string> XamlFiles()
        => Directory.EnumerateFiles(RepositoryPaths.Source("Humo.App"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static IEnumerable<(string Attribute, string Value)> UserFacingAttributeValues(string file)
    {
        // Attribute syntax only. Property-element syntax (<Label.Text>…</Label.Text>)
        // is not checked; it is rare, and a regex-free reader for it would cost
        // more than it catches. Noted in docs/testing.md.
        var document = XDocument.Load(file);

        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;

                if (UserFacingAttributes.Contains(name, StringComparer.Ordinal))
                {
                    yield return (name, attribute.Value);
                }
            }
        }
    }

    [Fact]
    public void No_ViewModel_returns_a_hardcoded_user_facing_string()
    {
        // Catches `return "Not enough cooks yet";` in Humo.Core. Deliberately
        // narrow: it flags string literals returned from a property or method,
        // which is how display text escapes into a ViewModel in practice.
        var violations = new List<string>();

        var viewModels = Directory.EnumerateFiles(
            RepositoryPaths.Source("Humo.Core", "ViewModels"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in viewModels)
        {
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                var match = ReturnedStringLiteral().Match(lines[index]);

                // Empty and single-character literals are separators and
                // formatting, not sentences shown to a user.
                if (match.Success && match.Groups["text"].Value.Length > 1)
                {
                    violations.Add(
                        $"{Path.GetRelativePath(RepositoryPaths.Root, file)}:{index + 1}: "
                        + $"\"{match.Groups["text"].Value}\"");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ViewModels returning literal strings. Resolve them through ILocalizer with an "
            + $"AppStrings key instead:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [GeneratedRegex("""=>\s*"(?<text>[^"]*)"|return\s+"(?<text>[^"]*)"\s*;""")]
    private static partial Regex ReturnedStringLiteral();
}
