using Avalonia.Media;
using Avalonia.Svg.Skia;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Library.Services;

/// <summary>
/// Downloads and caches Scryfall card-symbol SVGs, then vends them as Avalonia IImage objects.
/// On first run: fetches /symbology, downloads SVGs, writes manifest.json + SVG files.
/// On subsequent runs: loads immediately from disk without any network call.
/// </summary>
public sealed class SymbolService
{
    public static readonly SymbolService Instance = new();

    private readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "MTGLibrary/1.0" } }
    };

    private readonly ConcurrentDictionary<string, IImage> _images =
        new(StringComparer.OrdinalIgnoreCase);

    private Task? _loadTask;

    /// <summary>
    /// Fires on the UI thread each time a batch of symbols becomes available.
    /// Controls that display symbols should rebuild on this event.
    /// </summary>
    public event EventHandler? SymbolsUpdated;

    private SymbolService() { }

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MagicLibrary", "symbols");

    // Maps normalized key ("W", "W/U", "T" …) → svg filename ("W.svg", "WU.svg", "T.svg" …)
    private static string ManifestPath => Path.Combine(CacheDir, "manifest.json");

    // "{W}" → "W",  "{W/U}" → "W/U"
    private static string Normalize(string token) => token.Trim('{', '}');

    /// <summary>Starts background loading; safe to call multiple times.</summary>
    public void BeginLoad(CancellationToken ct = default)
    {
        if (_loadTask is { IsCompleted: false }) return;
        _loadTask = LoadAllAsync(ct);
    }

    /// <summary>Returns the cached IImage for a symbol token like "{W}" or "{T}", or null if not yet loaded.</summary>
    public IImage? TryGet(string symbolToken)
    {
        var key = Normalize(symbolToken);
        _images.TryGetValue(key, out var img);
        return img;
    }

    private async Task LoadAllAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);

        // ── Phase 1: load from disk cache immediately ──────────────────────────
        // Uses manifest.json (written on first successful network fetch) so keys
        // are correct even for hybrid symbols like {W/U} → WU.svg.
        if (File.Exists(ManifestPath))
        {
            try
            {
                var manifestJson = await File.ReadAllTextAsync(ManifestPath, ct);
                using var manifestDoc = JsonDocument.Parse(manifestJson);
                foreach (var prop in manifestDoc.RootElement.EnumerateObject())
                {
                    var key = prop.Name;
                    var fileName = prop.Value.GetString();
                    if (string.IsNullOrEmpty(fileName)) continue;
                    TryLoadFile(key, Path.Combine(CacheDir, fileName));
                }
            }
            catch { }

            if (_images.Count > 0)
                SymbolsUpdated?.Invoke(this, EventArgs.Empty);
        }

        // ── Phase 2: fetch fresh symbol list, download any missing SVGs ────────
        try
        {
            var json = await _http.GetStringAsync("https://api.scryfall.com/symbology", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;

            var entries = new List<(string symbol, string svgUri)>();
            var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("symbol", out var symEl) ||
                    !item.TryGetProperty("svg_uri", out var uriEl)) continue;
                var symbol = symEl.GetString();
                var uri = uriEl.GetString();
                if (symbol == null || uri == null) continue;

                var key = Normalize(symbol);
                var fileName = Path.GetFileName(new Uri(uri).LocalPath);
                manifest[key] = fileName;
                entries.Add((symbol, uri));
            }

            // Persist manifest so Phase 1 works on next launch
            try
            {
                var updated = JsonSerializer.Serialize(manifest);
                await File.WriteAllTextAsync(ManifestPath, updated, ct);
            }
            catch { }

            // Download missing SVGs in small batches (polite to Scryfall)
            const int batchSize = 4;
            var anyNew = false;
            for (var i = 0; i < entries.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = entries.GetRange(i, Math.Min(batchSize, entries.Count - i));
                var tasks = batch.ConvertAll(e => FetchOneAsync(e.symbol, e.svgUri, ct));
                var results = await Task.WhenAll(tasks);
                foreach (var added in results) if (added) anyNew = true;
            }

            if (anyNew || _images.Count > 0)
                SymbolsUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch { /* network not available — Phase 1 symbols are still usable */ }
    }

    // Returns true if a new symbol was added to _images.
    private async Task<bool> FetchOneAsync(string symbol, string svgUri, CancellationToken ct)
    {
        var key = Normalize(symbol);
        var fileName = Path.GetFileName(new Uri(svgUri).LocalPath);
        var filePath = Path.Combine(CacheDir, fileName);

        if (!File.Exists(filePath))
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(svgUri, ct);
                await File.WriteAllBytesAsync(filePath, bytes, ct);
            }
            catch { return false; }
        }

        return TryLoadFile(key, filePath);
    }

    private bool TryLoadFile(string key, string filePath)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            using var stream = File.OpenRead(filePath);
            var source = SvgSource.LoadFromStream(stream, null);
            if (source?.Picture == null) return false;
            _images[key] = new SvgImage { Source = source };
            return true;
        }
        catch { return false; }
    }
}
