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
}
