namespace Humo.App.Services;

/// <summary>
/// Gives XAML markup extensions access to the DI container.
/// <para>
/// A markup extension is constructed by the XAML parser, not by the container,
/// so it cannot take constructor dependencies. This is the narrow, documented
/// exception to "everything comes from DI" — it is used only by
/// <see cref="Localization.TranslateExtension"/>. ViewModels and services must
/// never resolve through it; they take constructor dependencies like everything
/// else.
/// </para>
/// </summary>
public static class ServiceHelper
{
    private static IServiceProvider? _services;

    public static void Initialize(IServiceProvider services) => _services = services;

    public static T GetRequiredService<T>() where T : notnull
        => (_services ?? throw new InvalidOperationException(
                $"{nameof(ServiceHelper)} was used before {nameof(Initialize)} was called."))
            .GetRequiredService<T>();
}
