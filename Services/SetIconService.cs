using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace Library.Services;

/// <summary>
/// Downloads and caches Scryfall set-icon SVGs and set names.
/// On first run: fetches /sets, downloads SVGs, writes manifest + names cache.
/// On subsequent runs: loads immediately from disk without network.
/// </summary>
public sealed class SetIconService
{
    public static readonly SetIconService Instance = new();

    private readonly HttpClient _http = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "MTGLibrary/1.0" },
            { "Accept", "application/json" },
        }
    };

    private readonly ConcurrentDictionary<string, IImage> _icons =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IImage> _iconsDark =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _names =
        new(StringComparer.OrdinalIgnoreCase);

    private Task? _loadTask;

    /// <summary>Fires on the UI thread when icons or names become available.</summary>
    public event EventHandler? SetsUpdated;

    private SetIconService() { }

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MagicLibrary", "seticons");

    private static string ManifestPath => Path.Combine(CacheDir, "manifest.json");
    private static string NamesPath    => Path.Combine(CacheDir, "names.json");

    /// <summary>Starts background loading; safe to call multiple times.</summary>
    public void BeginLoad(CancellationToken ct = default)
    {
        if (_loadTask is { IsCompleted: false }) return;
        _loadTask = Task.Run(() => LoadAllAsync(ct), ct);
    }

    public IImage? TryGetIcon(string setCode, bool dark = false)
    {
        var dict = dark ? _iconsDark : _icons;
        dict.TryGetValue(setCode, out var img);
        return img;
    }

    public string? TryGetName(string setCode)
    {
        _names.TryGetValue(setCode, out var name);
        return name;
    }

    private async Task LoadAllAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);

        // ── Phase 1: load from disk cache immediately ──────────────────────────
        if (File.Exists(NamesPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(NamesPath, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var name = prop.Value.GetString();
                    if (name != null) _names[prop.Name] = name;
                }
            }
            catch { }
        }

        if (File.Exists(ManifestPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(ManifestPath, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var fileName = prop.Value.GetString();
                    if (string.IsNullOrEmpty(fileName)) continue;
                    TryLoadFile(prop.Name, Path.Combine(CacheDir, fileName));
                }
            }
            catch { }

            if (!_icons.IsEmpty || !_names.IsEmpty)
                NotifyOnUIThread();
        }

        // ── Phase 2: fetch fresh list from Scryfall, download missing SVGs ─────
        try
        {
            var json = await _http.GetStringAsync("https://api.scryfall.com/sets", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;

            var entries   = new List<(string code, string iconUri)>();
            var manifest  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var namesDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("code", out var codeEl) ||
                    !item.TryGetProperty("name", out var nameEl) ||
                    !item.TryGetProperty("icon_svg_uri", out var uriEl)) continue;

                var code = codeEl.GetString();
                var name = nameEl.GetString();
                var uri  = uriEl.GetString();
                if (code == null || name == null || uri == null) continue;

                namesDict[code] = name;
                _names[code]    = name;

                var fileName = $"{code}.svg";
                manifest[code] = fileName;
                entries.Add((code, uri));
            }

            try
            {
                await File.WriteAllTextAsync(ManifestPath, JsonSerializer.Serialize(manifest), ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(NamesPath,    JsonSerializer.Serialize(namesDict), ct).ConfigureAwait(false);
            }
            catch { }

            // Download missing SVGs in batches
            const int batchSize = 8;
            var anyNew = false;
            for (var i = 0; i < entries.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch   = entries.GetRange(i, Math.Min(batchSize, entries.Count - i));
                var tasks   = batch.ConvertAll(e => FetchOneAsync(e.code, e.iconUri, ct));
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var added in results) if (added) anyNew = true;
            }

            if (anyNew || !_icons.IsEmpty || !_names.IsEmpty)
                NotifyOnUIThread();
        }
        catch (OperationCanceledException) { }
        catch { /* network unavailable — disk cache is still usable */ }
    }

    private async Task<bool> FetchOneAsync(string code, string iconUri, CancellationToken ct)
    {
        var filePath = Path.Combine(CacheDir, $"{code}.svg");

        if (!File.Exists(filePath))
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(iconUri, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
            }
            catch { return false; }
        }

        return TryLoadFile(code, filePath);
    }

    private bool TryLoadFile(string code, string filePath)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            var bytes = File.ReadAllBytes(filePath);

            using var stream = new MemoryStream(bytes);
            var source = SvgSource.LoadFromStream(stream, null);
            if (source?.Picture == null) return false;
            _icons[code] = new SvgImage { Source = source };

            var darkBytes = RecolorForDark(bytes);
            using var darkStream = new MemoryStream(darkBytes);
            var darkSource = SvgSource.LoadFromStream(darkStream, null);
            if (darkSource?.Picture != null)
                _iconsDark[code] = new SvgImage { Source = darkSource };

            return true;
        }
        catch { return false; }
    }

    // Replaces black fill values with a light grey suitable for dark backgrounds.
    private static byte[] RecolorForDark(byte[] svgBytes)
    {
        var text = System.Text.Encoding.UTF8.GetString(svgBytes);
        text = text.Replace("fill=\"#000000\"", "fill=\"#CCCCCC\"");
        text = text.Replace("fill=\"#000\"",    "fill=\"#CCCCCC\"");
        text = text.Replace("fill=\"black\"",   "fill=\"#CCCCCC\"");
        return System.Text.Encoding.UTF8.GetBytes(text);
    }

    private void NotifyOnUIThread() =>
        Dispatcher.UIThread.Post(() => SetsUpdated?.Invoke(this, EventArgs.Empty));
}
