using System.Globalization;
using Humo.Core.Localization;
using Humo.Core.Settings;
using Humo.Core.ViewModels;
using Humo.Shared.Units;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class AppSettingsViewModelTests
{
    private readonly Localizer _localizer = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();

    [Fact]
    public void Title_is_resolved_in_the_current_language()
    {
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        Assert.Equal("Settings", viewModel.Title);

        viewModel.SelectedLanguage = viewModel.LanguageOptions
            .First(option => option.Culture?.Name == "es");

        Assert.Equal("Ajustes", viewModel.Title);
    }

    [Fact]
    public void Selecting_a_language_persists_the_override()
    {
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        viewModel.SelectedLanguage = viewModel.LanguageOptions
            .First(option => option.Culture?.Name == "es");

        _settings.Received().LanguageOverride = Arg.Is<CultureInfo>(culture => culture.Name == "es");
    }

    [Fact]
    public void Choosing_follow_the_device_clears_the_override()
    {
        _settings.LanguageOverride.Returns(new CultureInfo("es"));
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        viewModel.SelectedLanguage = viewModel.LanguageOptions.First(option => option.Culture is null);

        _settings.Received().LanguageOverride = null;
    }

    [Fact]
    public void Switching_to_Spanish_does_not_change_the_temperature_unit()
    {
        // The rule this whole ViewModel exists to demonstrate.
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        viewModel.SelectedLanguage = viewModel.LanguageOptions
            .First(option => option.Culture?.Name == "es");

        Assert.True(viewModel.UseFahrenheit);
        Assert.Equal("°F", viewModel.TemperatureUnitSymbol);
        _settings.DidNotReceive().TemperatureUnit = Arg.Any<TemperatureUnit>();
    }

    [Fact]
    public void Switching_the_temperature_unit_does_not_change_the_language()
    {
        var viewModel = new AppSettingsViewModel(_localizer, _settings);
        viewModel.SelectedLanguage = viewModel.LanguageOptions
            .First(option => option.Culture?.Name == "es");
        _settings.ClearReceivedCalls();

        viewModel.UseFahrenheit = true;

        Assert.Equal("Ajustes", viewModel.Title);
        _settings.DidNotReceive().LanguageOverride = Arg.Any<CultureInfo?>();
    }

    [Fact]
    public void The_unit_symbol_follows_the_unit_setting()
    {
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        Assert.Equal("°C", viewModel.TemperatureUnitSymbol);

        viewModel.UseFahrenheit = true;

        Assert.Equal("°F", viewModel.TemperatureUnitSymbol);
        _settings.Received().TemperatureUnit = TemperatureUnit.Fahrenheit;
    }

    [Fact]
    public void A_stored_language_override_is_preselected()
    {
        _settings.LanguageOverride.Returns(new CultureInfo("es"));

        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        Assert.Equal("es", viewModel.SelectedLanguage.Culture?.Name);
    }

    [Fact]
    public void No_stored_override_preselects_follow_the_device()
    {
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        Assert.Null(viewModel.SelectedLanguage.Culture);
    }

    [Fact]
    public void A_stored_override_for_a_language_we_do_not_ship_preselects_follow_the_device()
    {
        // Nothing writes "de" today, but a future language that is later removed
        // would. The picker must land on a real option rather than nothing.
        _settings.LanguageOverride.Returns(new CultureInfo("de"));

        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        Assert.Null(viewModel.SelectedLanguage.Culture);
    }

    [Fact]
    public void Changing_language_notifies_every_string_the_page_shows()
    {
        // Each of these is a plain get-only property, so nothing raises change
        // notification for them automatically. If one is forgotten, that label
        // silently keeps the old language.
        var viewModel = new AppSettingsViewModel(_localizer, _settings);
        var notified = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notified.Add(args.PropertyName);

        viewModel.SelectedLanguage = viewModel.LanguageOptions.First(option => option.Culture?.Name == "es");

        Assert.Contains(nameof(AppSettingsViewModel.Title), notified);
        Assert.Contains(nameof(AppSettingsViewModel.TemperatureUnitExplanation), notified);
        Assert.Contains(nameof(AppSettingsViewModel.TemperatureUnitSymbol), notified);
    }

    [Fact]
    public void Reselecting_the_current_language_does_not_write_the_setting_again()
    {
        var viewModel = new AppSettingsViewModel(_localizer, _settings);
        var current = viewModel.SelectedLanguage;
        _settings.ClearReceivedCalls();

        viewModel.SelectedLanguage = current;

        _settings.DidNotReceive().LanguageOverride = Arg.Any<CultureInfo?>();
    }

    [Fact]
    public void Every_language_option_has_a_display_name_that_resolves()
    {
        // Options carry resource keys, not text. A key with no resource would
        // show up in the picker as the raw key.
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        foreach (var option in viewModel.LanguageOptions)
        {
            Assert.NotEqual(option.DisplayNameKey, _localizer[option.DisplayNameKey]);
        }
    }

    [Fact]
    public void The_explanation_and_title_are_translated_together()
    {
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        viewModel.SelectedLanguage = viewModel.LanguageOptions.First(option => option.Culture?.Name == "es");

        Assert.Equal("Ajustes", viewModel.Title);
        Assert.Equal("Independiente del idioma de la aplicación.", viewModel.TemperatureUnitExplanation);
    }

    [Fact]
    public void A_stored_Fahrenheit_preference_is_reflected_on_load()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);

        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        Assert.True(viewModel.UseFahrenheit);
        Assert.Equal("°F", viewModel.TemperatureUnitSymbol);
    }

    [Fact]
    public void Turning_Fahrenheit_back_off_returns_to_Celsius()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var viewModel = new AppSettingsViewModel(_localizer, _settings);

        viewModel.UseFahrenheit = false;

        _settings.Received().TemperatureUnit = TemperatureUnit.Celsius;
        Assert.Equal("°C", viewModel.TemperatureUnitSymbol);
    }
}
