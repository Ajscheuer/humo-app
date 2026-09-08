using System.Text.RegularExpressions;

namespace Humo.Conventions.Tests;

/// <summary>
/// A double hyphen inside an XML comment is not legal XML, and every XML file
/// this project builds from — the XAML pages, and
/// <c>Directory.Packages.props</c> — is one prose dash away from it.
/// <para>
/// It has a habit of being written by anyone explaining a decision in a comment,
/// and the way it surfaces is unhelpful: the props file silently stops applying
/// central package management, and the XAML shows up as an <c>XmlException</c>
/// thrown by a test about hardcoded strings. This test exists so the failure
/// names the file, the line, and the fix.
/// </para>
/// </summary>
public class XmlCommentTests
{
    /// <summary>Matches a comment body containing "--" anywhere but its closing "-->".</summary>
    private static readonly Regex Comment = new(@"<!--(.*?)-->", RegexOptions.Singleline);

    [Fact]
    public void No_XML_comment_contains_a_double_hyphen()
    {
        var offences = new List<string>();

        foreach (var file in XmlFiles())
        {
            var text = File.ReadAllText(file);

            foreach (Match match in Comment.Matches(text))
            {
                if (!match.Groups[1].Value.Contains("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offences.Add($"{Path.GetFileName(file)}:{line}");
            }
        }

        Assert.True(
            offences.Count == 0,
            "An XML comment cannot contain '--'. Rewrite the prose dash as a comma, "
            + "a full stop, or a single hyphen:\n  " + string.Join("\n  ", offences));
    }

    private static IEnumerable<string> XmlFiles()
    {
        var root = RepositoryPaths.Root;

        foreach (var pattern in new[] { "*.xaml", "*.csproj", "*.props", "*.targets" })
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                if (!file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    yield return file;
                }
            }
        }
    }
}
