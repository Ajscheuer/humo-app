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
}
