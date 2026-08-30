using System.ComponentModel;
using System.Globalization;
using Humo.Core.Localization;

namespace Humo.Core.Tests.Localization;

public class LocalizerTests
{
    [Fact]
    public void Defaults_to_English()
    {
        var localizer = new Localizer();

        Assert.Equal("en", localizer.CurrentCulture.Name);
        Assert.Equal("Settings", localizer[AppStrings.Settings_Title]);
    }

    [Fact]
    public void Resolves_Spanish_strings_when_the_culture_is_Spanish()
    {
        var localizer = new Localizer();

        localizer.SetCulture(new CultureInfo("es"));

        Assert.Equal("Ajustes", localizer[AppStrings.Settings_Title]);
        Assert.Equal("Guardar", localizer[AppStrings.Common_Save]);
    }

    [Fact]
    public void An_Argentine_device_gets_the_neutral_Spanish_resources()
    {
        // Spanish is authored neutral and flavored toward es-AR; there is no
        // separate es-AR resource file until a real divergence forces one.
        var localizer = new Localizer();

        localizer.SetCulture(new CultureInfo("es-AR"));

        Assert.Equal("es", localizer.CurrentCulture.Name);
        Assert.Equal("Ajustes", localizer[AppStrings.Settings_Title]);
    }

    [Fact]
    public void An_unsupported_language_falls_back_to_English()
    {
        var localizer = new Localizer();

        localizer.SetCulture(new CultureInfo("de-DE"));

        Assert.Equal("en", localizer.CurrentCulture.Name);
        Assert.Equal("Settings", localizer[AppStrings.Settings_Title]);
    }

    [Fact]
    public void Changing_the_culture_raises_CultureChanged_so_bound_UI_can_re_resolve()
    {
        var localizer = new Localizer();
        var raised = 0;
        localizer.CultureChanged += (_, _) => raised++;

        localizer.SetCulture(new CultureInfo("es"));
        localizer.SetCulture(new CultureInfo("es"));  // no change, no event

        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_missing_key_returns_the_key_rather_than_null_or_a_crash()
    {
        var localizer = new Localizer();

        Assert.Equal("Cook_NotAddedYet", localizer["Cook_NotAddedYet"]);
    }

    [Fact]
    public void Format_uses_the_current_culture_for_numbers()
    {
        var localizer = new Localizer();
        localizer.SetCulture(new CultureInfo("es"));

        // Spanish formats the decimal separator as a comma. Numbers follow
        // culture even though the temperature unit does not.
        Assert.Equal("6,8", string.Format(localizer.CurrentCulture, "{0:0.0}", 6.8));
    }

    [Theory]
    [InlineData("es-419")]
    [InlineData("es-MX")]
    [InlineData("ES")]
    public void Every_Spanish_variant_folds_onto_the_neutral_Spanish_resources(string cultureName)
    {
        var localizer = new Localizer();

        localizer.SetCulture(new CultureInfo(cultureName));

        Assert.Equal("es", localizer.CurrentCulture.Name);
    }

    [Fact]
    public void Passing_no_culture_falls_back_to_the_device_language()
    {
        // SetCulture(null) is how "follow the device" is expressed. It must
        // resolve through the device culture, not blow up.
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("es-AR");
            var localizer = new Localizer();

            localizer.SetCulture(null);

            Assert.Equal("es", localizer.CurrentCulture.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Changing_the_culture_notifies_the_indexer_so_bound_labels_re_resolve()
    {
        // {loc:Translate} is an indexer binding onto the localizer. Without this
        // notification an in-app language switch would leave every visible
        // string in the old language until the page was rebuilt.
        var localizer = new Localizer();
        var notified = new List<string?>();
        ((INotifyPropertyChanged)localizer).PropertyChanged += (_, args) => notified.Add(args.PropertyName);

        localizer.SetCulture(new CultureInfo("es"));

        Assert.Contains("Item[]", notified);
    }

    [Fact]
    public void Switching_language_twice_lands_back_on_the_original_strings()
    {
        var localizer = new Localizer();

        localizer.SetCulture(new CultureInfo("es"));
        localizer.SetCulture(new CultureInfo("en"));

        Assert.Equal("Settings", localizer[AppStrings.Settings_Title]);
    }

    [Fact]
    public void The_indexer_and_Get_resolve_identically()
    {
        var localizer = new Localizer();

        Assert.Equal(localizer.Get(AppStrings.Common_Save), localizer[AppStrings.Common_Save]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_key_is_a_programming_error_and_throws(string? key)
    {
        var localizer = new Localizer();

        Assert.ThrowsAny<ArgumentException>(() => localizer.Get(key!));
    }

    [Fact]
    public void Format_substitutes_arguments_using_the_current_culture()
    {
        var localizer = new Localizer(TestResources.WithFormatString());

        localizer.SetCulture(new CultureInfo("es"));

        Assert.Equal("Peso: 6,8", localizer.Format("Test_Weight", 6.8));
    }

    [Fact]
    public void Formatting_a_missing_key_returns_the_key_rather_than_throwing()
    {
        var localizer = new Localizer();

        Assert.Equal("Cook_Missing", localizer.Format("Cook_Missing", 1, 2));
    }

    private static class TestResources
    {
        /// <summary>
        /// A minimal in-memory resource set, so format-string behaviour can be
        /// tested without adding a fake string to the shipping resources.
        /// </summary>
        public static System.Resources.ResourceManager WithFormatString()
            => new FakeResourceManager(new Dictionary<string, string> { ["Test_Weight"] = "Peso: {0:0.0}" });

        private sealed class FakeResourceManager(IReadOnlyDictionary<string, string> values)
            : System.Resources.ResourceManager
        {
            public override string? GetString(string name, CultureInfo? culture)
                => values.GetValueOrDefault(name);
        }
    }
}
