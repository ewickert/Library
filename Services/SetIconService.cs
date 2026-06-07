using Avalonia.Svg.Skia;
using Avalonia.Threading;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace Library.Services;

/// <summary>
/// Downloads and caches Scryfall set-icon SVGs and set names.
/// Phase 1 (disk cache): reads manifest instantly, then parses each icon lazily on first request.
/// Phase 2 (network): fetches fresh list from Scryfall, downloads missing SVGs.
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

    // Standard MTG rarity fill colors (applied by replacing black fills in the SVG)
    private static readonly Dictionary<string, string> RarityColors =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["common"]      = "#FFFFFF",
        ["uncommon"]    = "#A9B8C0",
        ["rare"]        = "#C89B3C",
        ["mythic"]      = "#E87D1B",
        ["special"]     = "#C48ED6",
        ["bonus"]       = "#C48ED6",
        ["timeshifted"] = "#C48ED6",
    };

    // SvgSource is a plain class (not AvaloniaObject) — safe to create on any thread
    private readonly ConcurrentDictionary<string, SvgSource> _icons =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SvgSource> _iconsDark =
        new(StringComparer.OrdinalIgnoreCase);
    // Rarity-colored variants; keyed by "setcode:rarity" (e.g. "MH3:rare")
    private readonly ConcurrentDictionary<string, SvgSource> _iconsColored =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _names =
        new(StringComparer.OrdinalIgnoreCase);

    // Path map for lazy loading: set code → local SVG file path
    private readonly ConcurrentDictionary<string, string> _svgPaths =
        new(StringComparer.OrdinalIgnoreCase);

    // Tracks which codes/keys have a parse task in flight to avoid duplicate work
    private readonly ConcurrentDictionary<string, byte> _pendingParses =
        new(StringComparer.OrdinalIgnoreCase);

    // Debounce: collapse many rapid parse-complete notifications into one UI event
    private int _notifyPending;

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

    /// <summary>
    /// Returns the parsed SvgSource for setCode, or null if not yet loaded.
    /// When null, a background parse is queued and SetsUpdated fires when ready.
    /// Pass dark=true to get the light-grey recolored version for dark themes.
    /// Callers must wrap the returned SvgSource in a new SvgImage on the UI thread.
    /// </summary>
    public SvgSource? TryGetSource(string setCode, bool dark = false)
    {
        var dict = dark ? _iconsDark : _icons;
        if (dict.TryGetValue(setCode, out var src)) return src;
        EnqueueParse(setCode);
        return null;
    }

    /// <summary>
    /// Returns a rarity-colored SvgSource (common=white, uncommon=silver, rare=gold,
    /// mythic=orange). Falls back to the dark variant for unknown rarity values.
    /// Returns null while the background parse is in progress; SetsUpdated fires when ready.
    /// </summary>
    public SvgSource? TryGetRaritySource(string setCode, string rarity)
    {
        if (!RarityColors.TryGetValue(rarity, out var color))
            return TryGetSource(setCode, dark: true);

        var key = $"{setCode.ToUpperInvariant()}:{rarity.ToLowerInvariant()}";
        if (_iconsColored.TryGetValue(key, out var src)) return src;
        EnqueueColoredParse(setCode, key, color);
        return null;
    }

    public string? TryGetName(string setCode)
    {
        _names.TryGetValue(setCode, out var name);
        return name;
    }

    private void EnqueueParse(string code)
    {
        if (!_svgPaths.ContainsKey(code)) return;       // not in manifest yet
        if (!_pendingParses.TryAdd(code, 0)) return;    // already queued

        Task.Run(() =>
        {
            if (_svgPaths.TryGetValue(code, out var path))
                TryLoadFile(code, path);
            _pendingParses.TryRemove(code, out _);
            NotifyOnUIThread();
        });
    }

    private void EnqueueColoredParse(string setCode, string key, string hexColor)
    {
        if (!_svgPaths.ContainsKey(setCode)) return;
        if (!_pendingParses.TryAdd(key, 0)) return;

        Task.Run(() =>
        {
            if (_svgPaths.TryGetValue(setCode, out var path) && File.Exists(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var recolored = RecolorWith(bytes, hexColor);
                    using var ms = new MemoryStream(recolored);
                    var source = SvgSource.LoadFromStream(ms, null);
                    if (source?.Picture != null)
                        _iconsColored[key] = source;
                }
                catch { }
            }
            _pendingParses.TryRemove(key, out _);
            NotifyOnUIThread();
        });
    }

    private async Task LoadAllAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);

        // Phase 1: read names + manifest into memory maps — no SVG parsing yet
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
                    if (!string.IsNullOrEmpty(fileName))
                        _svgPaths[prop.Name] = Path.Combine(CacheDir, fileName);
                }
            }
            catch { }

            // Names + path map ready — controls can now trigger lazy icon loads
            if (!_svgPaths.IsEmpty || !_names.IsEmpty)
                NotifyOnUIThread();
        }

        // Phase 2: fetch fresh list from Scryfall, download any missing SVGs
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

        // Make the file discoverable to lazy loads before parsing it
        _svgPaths[code] = filePath;

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
            _icons[code] = source;

            var darkBytes = RecolorForDark(bytes);
            using var darkStream = new MemoryStream(darkBytes);
            var darkSource = SvgSource.LoadFromStream(darkStream, null);
            if (darkSource?.Picture != null)
                _iconsDark[code] = darkSource;

            return true;
        }
        catch { return false; }
    }

    private static byte[] RecolorForDark(byte[] svgBytes) => RecolorWith(svgBytes, "#CCCCCC");

    // Recolors a Scryfall set-icon SVG to hexColor.
    // Two cases exist in the wild:
    //   • SVGs with no fill attribute (751/1043) — default SVG fill is black; fix by
    //     injecting fill on the root <svg> element so all paths inherit the target color.
    //   • SVGs with explicit fills (#000, #000000, black, #444) — replaced directly.
    private static byte[] RecolorWith(byte[] svgBytes, string hexColor)
    {
        var text = System.Text.Encoding.UTF8.GetString(svgBytes);

        // Inject fill on the root element — covers paths that rely on SVG's default black.
        // No Scryfall icon has fill= on the <svg> element itself, so no duplicate attribute.
        text = text.Replace("<svg ", $"<svg fill=\"{hexColor}\" ");

        // Replace explicit dark fills that would otherwise override the inherited color.
        text = text.Replace("fill=\"#000000\"", $"fill=\"{hexColor}\"");
        text = text.Replace("fill=\"#000\"",    $"fill=\"{hexColor}\"");
        text = text.Replace("fill=\"black\"",   $"fill=\"{hexColor}\"");
        text = text.Replace("fill=\"#444444\"", $"fill=\"{hexColor}\"");
        text = text.Replace("fill=\"#444\"",    $"fill=\"{hexColor}\"");

        return System.Text.Encoding.UTF8.GetBytes(text);
    }

    // Collapses rapid back-to-back parse completions into a single UI notification.
    private void NotifyOnUIThread()
    {
        if (Interlocked.Exchange(ref _notifyPending, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _notifyPending, 0);
            SetsUpdated?.Invoke(this, EventArgs.Empty);
        });
    }
}
