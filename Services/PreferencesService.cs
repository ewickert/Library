using System;
using System.IO;
using System.Text.Json;

namespace Library.Services;

/// <summary>
/// Persists per-user view preferences (view modes, zoom levels) across sessions.
/// Settings are stored as JSON at ~/AppData/MagicLibrary/preferences.json.
/// All property setters auto-save on change.
/// </summary>
public sealed class PreferencesService
{
    public static PreferencesService Instance { get; } = new();

    private Prefs _p = new();

    private PreferencesService() => Load();

    // ── Decks view ────────────────────────────────────────────────────────────
    public bool DeckIsGridView
    {
        get => _p.DeckIsGridView;
        set { _p.DeckIsGridView = value; Save(); }
    }

    public bool DeckIsSortedView
    {
        get => _p.DeckIsSortedView;
        set { _p.DeckIsSortedView = value; Save(); }
    }

    public double DeckGridZoom
    {
        get => _p.DeckGridZoom;
        set { _p.DeckGridZoom = Math.Clamp(value, 0.5, 3.0); Save(); }
    }

    // ── Collection view ───────────────────────────────────────────────────────
    public bool CollectionIsGridView
    {
        get => _p.CollectionIsGridView;
        set { _p.CollectionIsGridView = value; Save(); }
    }

    public double CollectionGridZoom
    {
        get => _p.CollectionGridZoom;
        set { _p.CollectionGridZoom = Math.Clamp(value, 0.5, 3.0); Save(); }
    }

    // ── I/O ───────────────────────────────────────────────────────────────────
    private void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                _p = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { _p = new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_p,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* preferences are cosmetic — ignore write failures */ }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MagicLibrary", "preferences.json");

    private sealed class Prefs
    {
        public bool   DeckIsGridView        { get; set; } = false;
        public bool   DeckIsSortedView      { get; set; } = false;
        public double DeckGridZoom          { get; set; } = 1.0;
        public bool   CollectionIsGridView  { get; set; } = false;
        public double CollectionGridZoom    { get; set; } = 1.0;
    }
}
