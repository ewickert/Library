using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using System;
using System.IO;
using System.Linq;

namespace Library.Services;

/// <summary>
/// Manages the active visual theme. Call <see cref="Initialize"/> once in
/// <c>App.OnFrameworkInitializationCompleted</c>, then <see cref="Apply"/> to switch themes.
/// Adding a new theme requires:
///   1. A Styles AXAML file in <c>Themes/</c>
///   2. A new entry in <see cref="AvailableThemes"/>
///   3. A new <c>case</c> in the <see cref="Apply"/> switch
/// </summary>
public sealed class ThemeService
{
    public static ThemeService Instance { get; } = new();

    public static string[] GetAvailableThemes()
    {
        var themes = new[] { "Default", "Liquid Glass", "Material" }.ToList();

        if (OperatingSystem.IsIOS())
            themes.Insert(0, "Native (iOS)");

        return themes.ToArray();
    }

    private readonly Styles _slot = new();
    private IStyle? _current;

    public string CurrentThemeName { get; private set; } = "Default";

    private ThemeService() { }

    /// <summary>Called once during app startup to register the theme slot.</summary>
    public void Initialize(Application app)
    {
        app.Styles.Add(_slot);
    }

    /// <summary>Switches to the named theme and persists the choice.</summary>
    public void Apply(string themeName)
    {
        CurrentThemeName = themeName;

        if (Application.Current is { } app)
        {
            if (themeName == "Native (iOS)" && OperatingSystem.IsIOS())
            {
                app.RequestedThemeVariant = ThemeVariant.Default;

                if (_current != null)
                {
                    _slot.Remove(_current);
                    _current = null;
                }

                Persist(themeName);
                return;
            }

            // Force dark mode for non-native themes (custom themes are dark)
            app.RequestedThemeVariant = ThemeVariant.Dark;
        }

        var uri = themeName switch
        {
            "Liquid Glass" => new Uri("avares://Library/Themes/LiquidGlassTheme.axaml"),
            "Material"     => new Uri("avares://Library/Themes/MaterialTheme.axaml"),
            _              => new Uri("avares://Library/Themes/DefaultTheme.axaml"),
        };

        var newStyle = new StyleInclude(new Uri("avares://Library/")) { Source = uri };

        // Swap atomically: add new first, then remove old to minimise flash
        _slot.Add(newStyle);
        if (_current != null) _slot.Remove(_current);
        _current = newStyle;

        Persist(themeName);
    }

    /// <summary>Restores the last saved theme, or Default if none saved.</summary>
    public void LoadSaved()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var saved = File.ReadAllText(path).Trim();
                if (GetAvailableThemes().Contains(saved))
                {
                    Apply(saved);
                    return;
                }
            }
        }
        catch { /* fall through to default */ }

        Apply("Default");
    }

    private static void Persist(string name)
    {
        try { File.WriteAllText(SettingsPath, name); }
        catch { /* settings are cosmetic — ignore write failures */ }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MagicLibrary", "theme.txt");
}
