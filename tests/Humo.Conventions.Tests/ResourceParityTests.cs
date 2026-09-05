using System.Xml.Linq;
using Humo.Core.Localization;

namespace Humo.Conventions.Tests;

/// <summary>
/// Mechanical enforcement of the localization rule in CLAUDE.md: an English
/// string added without its Spanish counterpart fails the build instead of
/// shipping as English text inside a Spanish UI.
/// </summary>
public class ResourceParityTests
{
    private static readonly IReadOnlyDictionary<string, string> English = LoadResx("AppResources.resx");
    private static readonly IReadOnlyDictionary<string, string> Spanish = LoadResx("AppResources.es.resx");

    /// <summary>
    /// Keys whose Spanish value is intentionally identical to the English one —
    /// the product name, unit symbols, and language names shown in their own
    /// language. Anything not listed here that matches its English value is
    /// treated as an untranslated string.
    /// </summary>
    private static readonly HashSet<string> IntentionallyIdentical =
    [
        AppStrings.App_Name,
        AppStrings.Settings_Language_English,
        AppStrings.Settings_Language_Spanish,
        AppStrings.Unit_Celsius_Short,
        AppStrings.Unit_Fahrenheit_Short,
        AppStrings.Unit_Kilograms_Short,
        AppStrings.Unit_Pounds_Short,
        // Spanish-speaking barbecue borrows the English cut name; "pecho" is the
        // anatomical word, not what a parrillero calls the cut. See the glossary
        // in docs/product-spec.md §7.1.
        AppStrings.MeatType_Brisket,

        // "Offset" is the word in Spanish too, per the glossary. "Parrilla" and
        // "kamado" are the rigs' only names in either language.
        AppStrings.EquipmentType_Offset,
        AppStrings.EquipmentType_Kamado,
        AppStrings.EquipmentType_Parrilla,

        // South American species: the Spanish name is the only name.
        AppStrings.WoodType_Quebracho,
        AppStrings.WoodType_Espinillo,

        // A loanword in general Spanish use, and the litre symbol, which is a
        // unit symbol rather than a word.
        AppStrings.FuelForm_Pellets,
        AppStrings.Unit_Litres_Short,
    ];

    [Fact]
    public void Every_English_string_has_a_Spanish_translation()
    {
        var missing = English.Keys.Except(Spanish.Keys).Order().ToList();

        Assert.True(
            missing.Count == 0,
            $"These keys exist in AppResources.resx but not AppResources.es.resx: {string.Join(", ", missing)}. "
            + "Add the Spanish string in the same commit (CLAUDE.md).");
    }

    [Fact]
    public void No_Spanish_string_exists_without_an_English_counterpart()
    {
        var orphaned = Spanish.Keys.Except(English.Keys).Order().ToList();

        Assert.True(
            orphaned.Count == 0,
            $"These keys exist in AppResources.es.resx but not AppResources.resx: {string.Join(", ", orphaned)}. "
            + "English is the neutral fallback, so every key must exist there.");
    }

    [Fact]
    public void No_Spanish_string_is_left_as_untranslated_English()
    {
        var untranslated = English
            .Where(pair => !IntentionallyIdentical.Contains(pair.Key))
            .Where(pair => Spanish.TryGetValue(pair.Key, out var es) && es == pair.Value)
            .Select(pair => pair.Key)
            .Order()
            .ToList();

        Assert.True(
            untranslated.Count == 0,
            $"These Spanish strings are identical to their English values: {string.Join(", ", untranslated)}. "
            + "Translate them, or add the key to IntentionallyIdentical with a reason.");
    }

    [Fact]
    public void Every_AppStrings_constant_resolves_in_both_languages()
    {
        var declared = typeof(AppStrings)
            .GetFields()
            .Where(field => field is { IsLiteral: true, IsInitOnly: false })
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        var missingEnglish = declared.Where(key => !English.ContainsKey(key)).Order().ToList();
        var missingSpanish = declared.Where(key => !Spanish.ContainsKey(key)).Order().ToList();

        Assert.True(
            missingEnglish.Count == 0 && missingSpanish.Count == 0,
            $"AppStrings constants with no resource. Missing in English: [{string.Join(", ", missingEnglish)}]; "
            + $"missing in Spanish: [{string.Join(", ", missingSpanish)}].");
    }

    [Fact]
    public void No_string_resource_is_empty()
    {
        var empty = English.Concat(Spanish)
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .Distinct()
            .Order()
            .ToList();

        Assert.True(empty.Count == 0, $"Empty resource values: {string.Join(", ", empty)}.");
    }

    private static Dictionary<string, string> LoadResx(string fileName)
    {
        var path = RepositoryPaths.Source("Humo.Core", "Resources", "Strings", fileName);

        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty);
    }
}
