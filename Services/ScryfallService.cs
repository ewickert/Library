using Avalonia.Media.Imaging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Library.Services;

public class ScryfallService
{
    /// <summary>Set when the single application instance is created. Used by controls inside ToolTip popups that lack a MainWindow visual root.</summary>
    public static ScryfallService? Instance { get; private set; }

    // Separate clients: one for the JSON API, one for image CDN (different base URL)
    private static readonly HttpClient _api;
    private static readonly HttpClient _cdn;

    // Scryfall rate limit: max 10 req/s — enforce 120ms minimum between API calls
    private static readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private static DateTime _lastApiCall = DateTime.MinValue;
    private const int MinMsBetweenCalls = 120;

    static ScryfallService()
    {
        _api = new HttpClient { BaseAddress = new Uri("https://api.scryfall.com/") };
        _api.DefaultRequestHeaders.UserAgent.ParseAdd("MagicLibraryApp/1.0");
        _api.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _cdn = new HttpClient();
        _cdn.DefaultRequestHeaders.UserAgent.ParseAdd("MagicLibraryApp/1.0");
    }

    private readonly string _cacheDir;
    private readonly string _jsonCacheDir;

    // In-memory JSON cache — avoids a second API hit when image + text are fetched for the same card
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string>
        _jsonCache = new(StringComparer.Ordinal);

    public string ImageCacheDirectory => _cacheDir;

    public ScryfallService()
    {
        Instance = this;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MagicLibrary", "ImageCache");
        _jsonCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MagicLibrary", "CardCache");
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(_jsonCacheDir);
    }

    /// <summary>
    /// Returns the raw Scryfall JSON for a card by ID.
    /// Results are cached in memory and on disk so concurrent callers (image + text) share one API hit.
    /// </summary>
    private async Task<string?> GetCardJsonAsync(string scryfallId, CancellationToken ct)
    {
        if (_jsonCache.TryGetValue(scryfallId, out var cached)) return cached;

        var diskPath = Path.Combine(_jsonCacheDir, $"{scryfallId}.json");
        if (File.Exists(diskPath))
        {
            try
            {
                var disk = await File.ReadAllTextAsync(diskPath, ct);
                _jsonCache[scryfallId] = disk;
                return disk;
            }
            catch { }
        }

        var json = await ApiGetAsync($"cards/{scryfallId}", ct);
        if (json == null) return null;

        _jsonCache[scryfallId] = json;
        try { await File.WriteAllTextAsync(diskPath, json, ct); } catch { }
        return json;
    }

    public async Task<Bitmap?> GetCardImageAsync(string scryfallId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scryfallId)) return null;

        var cachePath = Path.Combine(_cacheDir, $"{scryfallId}.jpg");
        if (File.Exists(cachePath))
        {
            try { return new Bitmap(cachePath); } catch { File.Delete(cachePath); }
        }

        var imageUrl = await GetImageUrlFromScryfallAsync(scryfallId, ct);
        if (imageUrl == null) return null;

        return await DownloadAndCacheAsync(imageUrl, cachePath, ct);
    }

    public async Task<Bitmap?> GetCardArtCropAsync(string scryfallId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scryfallId)) return null;

        var cachePath = Path.Combine(_cacheDir, $"{scryfallId}_art.jpg");
        if (File.Exists(cachePath))
        {
            try { return new Bitmap(cachePath); } catch { File.Delete(cachePath); }
        }

        try
        {
            var json = await ApiGetAsync($"cards/{scryfallId}", ct);
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? artUrl = null;
            if (root.TryGetProperty("image_uris", out var uris) && uris.TryGetProperty("art_crop", out var art))
                artUrl = art.GetString();
            else if (root.TryGetProperty("card_faces", out var faces) && faces.GetArrayLength() > 0)
            {
                var face = faces[0];
                if (face.TryGetProperty("image_uris", out var faceUris) && faceUris.TryGetProperty("art_crop", out var faceArt))
                    artUrl = faceArt.GetString();
            }
            if (artUrl == null) return null;
            return await DownloadAndCacheAsync(artUrl, cachePath, ct);
        }
        catch { return null; }
    }

    public async Task<Bitmap?> GetCardImageByCollectorAsync(string setCode, string collectorNumber, bool foil, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(setCode) || string.IsNullOrWhiteSpace(collectorNumber)) return null;

        var cacheKey = $"{setCode.ToLower()}_{collectorNumber}_{(foil ? "foil" : "normal")}";
        var cachePath = Path.Combine(_cacheDir, $"{cacheKey}.jpg");
        if (File.Exists(cachePath))
        {
            try { return new Bitmap(cachePath); } catch { File.Delete(cachePath); }
        }

        try
        {
            var url = $"cards/{Uri.EscapeDataString(setCode.ToLower())}/{Uri.EscapeDataString(collectorNumber)}";
            var json = await ApiGetAsync(url, ct);
            if (json == null) return null;

            using var doc = JsonDocument.Parse(json);
            var imageUrl = ExtractImageUrl(doc.RootElement);
            if (imageUrl == null) return null;

            return await DownloadAndCacheAsync(imageUrl, cachePath, ct);
        }
        catch { return null; }
    }

    private async Task<string?> GetImageUrlFromScryfallAsync(string scryfallId, CancellationToken ct)
    {
        try
        {
            var json = await GetCardJsonAsync(scryfallId, ct);
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            return ExtractImageUrl(doc.RootElement);
        }
        catch { return null; }
    }

    private async Task<Bitmap?> DownloadAndCacheAsync(string imageUrl, string cachePath, CancellationToken ct)
    {
        try
        {
            var bytes = await _cdn.GetByteArrayAsync(imageUrl, ct);
            await File.WriteAllBytesAsync(cachePath, bytes, ct);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch { return null; }
    }

    // Rate-limited API wrapper
    private static async Task<string?> ApiGetAsync(string relativeUrl, CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var elapsed = (DateTime.UtcNow - _lastApiCall).TotalMilliseconds;
            if (elapsed < MinMsBetweenCalls)
                await Task.Delay((int)(MinMsBetweenCalls - elapsed), ct);

            var response = await _api.GetAsync(relativeUrl, ct);
            _lastApiCall = DateTime.UtcNow;

            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(ct);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    private static string? ExtractImageUrl(JsonElement card)
    {
        if (card.TryGetProperty("image_uris", out var imageUris))
        {
            if (imageUris.TryGetProperty("normal", out var normal)) return normal.GetString();
            if (imageUris.TryGetProperty("large", out var large)) return large.GetString();
            if (imageUris.TryGetProperty("small", out var small)) return small.GetString();
        }
        // Double-faced card: use first face
        if (card.TryGetProperty("card_faces", out var faces) && faces.GetArrayLength() > 0)
        {
            var face = faces[0];
            if (face.TryGetProperty("image_uris", out var faceUris))
            {
                if (faceUris.TryGetProperty("normal", out var normal)) return normal.GetString();
                if (faceUris.TryGetProperty("large", out var large)) return large.GetString();
            }
        }
        return null;
    }

    public async Task<ScryfallCardData?> SearchCardAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var json = await ApiGetAsync($"cards/named?fuzzy={Uri.EscapeDataString(name)}", ct);
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            return ParseCardData(doc.RootElement);
        }
        catch { return null; }
    }

    public Task<ScryfallPageResult> SearchCardsAsync(string query, CancellationToken ct = default)
        => FetchSearchPageAsync($"cards/search?q={Uri.EscapeDataString(query)}&order=name", ct);

    public Task<ScryfallPageResult> SearchCardsNextPageAsync(string nextPageUrl, CancellationToken ct = default)
        => FetchSearchPageAsync(nextPageUrl, ct);

    private async Task<ScryfallPageResult> FetchSearchPageAsync(string relativeUrl, CancellationToken ct)
    {
        try
        {
            var json = await ApiGetAsync(relativeUrl, ct);
            if (json == null) return new ScryfallPageResult([], false, null);
            using var doc = JsonDocument.Parse(json);
            var cards = new List<ScryfallCardData>();
            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var card in data.EnumerateArray())
                {
                    var parsed = ParseCardData(card);
                    if (parsed != null) cards.Add(parsed);
                }

            string? nextUrl = null;
            if (doc.RootElement.TryGetProperty("has_more", out var hasMore) && hasMore.GetBoolean()
                && doc.RootElement.TryGetProperty("next_page", out var nextPage))
            {
                var next = nextPage.GetString();
                const string baseUrl = "https://api.scryfall.com/";
                if (next != null)
                    nextUrl = next.StartsWith(baseUrl) ? next[baseUrl.Length..] : next;
            }

            return new ScryfallPageResult(cards, nextUrl != null, nextUrl);
        }
        catch { return new ScryfallPageResult([], false, null); }
    }

    /// <summary>
    /// Runs a full Scryfall query and returns the set of matching Scryfall IDs and card names,
    /// paginating up to 6 pages (~1050 cards). Used to filter the local collection by Scryfall syntax.
    /// </summary>
    public async Task<ScryfallSearchResult> SearchMatchingIdsAsync(string query, CancellationToken ct = default)
    {
        var ids   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return new ScryfallSearchResult(ids, names);

        const string baseUrl = "https://api.scryfall.com/";
        string? relUrl = $"cards/search?q={Uri.EscapeDataString(query)}&order=name&unique=cards";

        try
        {
            for (int page = 0; page < 6 && relUrl != null; page++)
            {
                if (ct.IsCancellationRequested) break;
                var json = await ApiGetAsync(relUrl, ct);
                if (json == null) break;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var data))
                    foreach (var card in data.EnumerateArray())
                    {
                        if (card.TryGetProperty("id",   out var idEl))   ids.Add(idEl.GetString() ?? string.Empty);
                        if (card.TryGetProperty("name", out var nameEl)) names.Add(nameEl.GetString() ?? string.Empty);
                    }

                relUrl = null;
                if (root.TryGetProperty("has_more", out var hasMore) && hasMore.GetBoolean() &&
                    root.TryGetProperty("next_page", out var nextPage))
                {
                    var next = nextPage.GetString();
                    if (next != null && next.StartsWith(baseUrl))
                        relUrl = next[baseUrl.Length..];
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { /* return partial results on network error */ }

        return new ScryfallSearchResult(ids, names);
    }

    /// <summary>Returns all printings of a card by exact name using Scryfall unique=prints.</summary>
    public async Task<List<ScryfallCardData>> GetAlternatePrintingsAsync(string cardName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return [];
        try
        {
            var query = $"!\"{cardName}\"";
            var url = $"cards/search?q={Uri.EscapeDataString(query)}&unique=prints&order=released";
            var results = new List<ScryfallCardData>();

            while (url != null)
            {
                ct.ThrowIfCancellationRequested();
                var json = await ApiGetAsync(url, ct);
                if (json == null) break;

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data))
                    foreach (var card in data.EnumerateArray())
                    {
                        var parsed = ParseCardData(card);
                        if (parsed != null) results.Add(parsed);
                    }

                url = null;
                if (doc.RootElement.TryGetProperty("has_more", out var hasMore) && hasMore.GetBoolean() &&
                    doc.RootElement.TryGetProperty("next_page", out var nextPage))
                {
                    var nextUrl = nextPage.GetString();
                    if (nextUrl?.StartsWith("https://api.scryfall.com/") == true)
                        url = nextUrl["https://api.scryfall.com/".Length..];
                }
            }

            return results;
        }
        catch (OperationCanceledException) { return []; }
        catch { return []; }
    }

    /// <summary>
    /// Fetches a single page of alternate printings. Pass the card name for the first page;
    /// pass <paramref name="nextPageUrl"/> on subsequent calls. Returns the cards on this page
    /// and the absolute URL of the next page (null if this is the last page).
    /// </summary>
    public async Task<(List<ScryfallCardData> Cards, string? NextPageUrl, int TotalCards)>
        GetAlternatePrintingsPageAsync(string cardName, string? nextPageUrl, CancellationToken ct = default)
    {
        try
        {
            string relativeUrl;
            if (nextPageUrl != null)
            {
                // Strip base so ApiGetAsync can add it back
                relativeUrl = nextPageUrl.StartsWith("https://api.scryfall.com/")
                    ? nextPageUrl["https://api.scryfall.com/".Length..]
                    : nextPageUrl;
            }
            else
            {
                var query = $"!\"{cardName}\"";
                relativeUrl = $"cards/search?q={Uri.EscapeDataString(query)}&unique=prints&order=released";
            }

            var json = await ApiGetAsync(relativeUrl, ct);
            if (json == null) return ([], null, 0);

            using var doc = JsonDocument.Parse(json);
            var cards = new List<ScryfallCardData>();
            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var card in data.EnumerateArray())
                {
                    var parsed = ParseCardData(card);
                    if (parsed != null) cards.Add(parsed);
                }

            int totalCards = doc.RootElement.TryGetProperty("total_cards", out var tc)
                ? tc.GetInt32() : cards.Count;

            string? next = null;
            if (doc.RootElement.TryGetProperty("has_more", out var hasMore) && hasMore.GetBoolean() &&
                doc.RootElement.TryGetProperty("next_page", out var np))
                next = np.GetString();

            return (cards, next, totalCards);
        }
        catch (OperationCanceledException) { return ([], null, 0); }
        catch { return ([], null, 0); }
    }

    private static ScryfallCardData? ParseCardData(JsonElement card)
    {
        try
        {
            // color_identity is an array like ["W","U","B"]
            string colorIdentity = string.Empty;
            if (card.TryGetProperty("color_identity", out var ci))
                colorIdentity = string.Join(",", ci.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0));

            // mana_cost may be absent on tokens/lands
            string manaCost = string.Empty;
            if (card.TryGetProperty("mana_cost", out var mc))
                manaCost = mc.GetString() ?? string.Empty;

            // type_line may be absent on some objects
            string typeLine = string.Empty;
            if (card.TryGetProperty("type_line", out var tl))
                typeLine = tl.GetString() ?? string.Empty;

            return new ScryfallCardData
            {
                ScryfallId = card.GetProperty("id").GetString() ?? string.Empty,
                Name = card.GetProperty("name").GetString() ?? string.Empty,
                SetCode = card.GetProperty("set").GetString() ?? string.Empty,
                SetName = card.GetProperty("set_name").GetString() ?? string.Empty,
                CollectorNumber = card.GetProperty("collector_number").GetString() ?? string.Empty,
                Rarity = card.GetProperty("rarity").GetString() ?? string.Empty,
                ColorIdentity = colorIdentity,
                ManaCost = manaCost,
                TypeLine = typeLine,
            };
        }
        catch { return null; }
    }

    /// <summary>Returns the color identity string (e.g. "W,U,B") for a card by Scryfall ID.</summary>
    public async Task<string?> GetColorIdentityAsync(string scryfallId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scryfallId)) return null;
        try
        {
            var json = await ApiGetAsync($"cards/{scryfallId}", ct);
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("color_identity", out var ci))
                return string.Join(",", ci.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0));
            return string.Empty;
        }
        catch { return null; }
    }

    /// <summary>
    /// Fetches ColorIdentity, ManaCost, and TypeLine for every card in <paramref name="cards"/>
    /// that is missing at least one of those fields, writing results back via <paramref name="onResult"/>.
    /// Reports progress as (completed, total).
    /// </summary>
    public async Task BackfillCardMetadataAsync(
        IReadOnlyList<Library.Models.Card> cards,
        Action<Library.Models.Card, string?, string?, string?> onResult,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        int total = cards.Count;
        int done  = 0;

        foreach (var card in cards)
        {
            if (ct.IsCancellationRequested) break;

            string? ci = null, mc = null, tl = null;

            // Prefer fetching by ScryfallId (single call, all fields at once)
            if (!string.IsNullOrEmpty(card.ScryfallId))
            {
                try
                {
                    var json = await ApiGetAsync($"cards/{card.ScryfallId}", ct);
                    if (json != null)
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("color_identity", out var ciEl))
                            ci = string.Join(",", ciEl.EnumerateArray()
                                .Select(e => e.GetString() ?? "").Where(s => s.Length > 0));
                        if (root.TryGetProperty("mana_cost", out var mcEl))
                            mc = mcEl.GetString();
                        if (root.TryGetProperty("type_line", out var tlEl))
                            tl = tlEl.GetString();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* leave nulls, skip */ }
            }
            else if (!string.IsNullOrEmpty(card.SetCode) && !string.IsNullOrEmpty(card.CollectorNumber))
            {
                // Fall back to set+collector-number lookup
                try
                {
                    var json = await ApiGetAsync(
                        $"cards/{Uri.EscapeDataString(card.SetCode.ToLower())}/{Uri.EscapeDataString(card.CollectorNumber)}", ct);
                    if (json != null)
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("color_identity", out var ciEl))
                            ci = string.Join(",", ciEl.EnumerateArray()
                                .Select(e => e.GetString() ?? "").Where(s => s.Length > 0));
                        if (root.TryGetProperty("mana_cost", out var mcEl))
                            mc = mcEl.GetString();
                        if (root.TryGetProperty("type_line", out var tlEl))
                            tl = tlEl.GetString();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }

            onResult(card, ci, mc, tl);
            progress?.Report((++done, total));
        }
    }

    /// <summary>
    public async Task<CardTextData?> GetCardTextDataAsync(Models.Card card, CancellationToken ct = default)
    {
        try
        {
            string? json = null;
            if (!string.IsNullOrEmpty(card.ScryfallId))
                json = await GetCardJsonAsync(card.ScryfallId, ct);
            else if (!string.IsNullOrEmpty(card.SetCode) && !string.IsNullOrEmpty(card.CollectorNumber))
                json = await ApiGetAsync($"cards/{Uri.EscapeDataString(card.SetCode.ToLower())}/{Uri.EscapeDataString(card.CollectorNumber)}", ct);

            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? oracleText = null;
            string? power = null, toughness = null, loyalty = null, defense = null;

            if (root.TryGetProperty("oracle_text", out var ot))
                oracleText = ot.GetString();
            else if (root.TryGetProperty("card_faces", out var faces) && faces.GetArrayLength() > 0)
            {
                // Double-faced: join both faces with a separator
                var parts = new List<string>();
                foreach (var face in faces.EnumerateArray())
                {
                    if (face.TryGetProperty("name", out var fn)) parts.Add($"[{fn.GetString()}]");
                    if (face.TryGetProperty("oracle_text", out var fo)) parts.Add(fo.GetString() ?? string.Empty);
                }
                oracleText = string.Join("\n\n", parts.Where(p => p.Length > 0));
            }

            if (root.TryGetProperty("power", out var pw)) power = pw.GetString();
            if (root.TryGetProperty("toughness", out var tg)) toughness = tg.GetString();
            if (root.TryGetProperty("loyalty", out var ly)) loyalty = ly.GetString();
            if (root.TryGetProperty("defense", out var df)) defense = df.GetString();

            return new CardTextData(oracleText, power, toughness, loyalty, defense);
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns the current USD market price for a card from Scryfall.
    /// Uses <c>prices.usd_foil</c> when <paramref name="foil"/> is true, otherwise <c>prices.usd</c>.
    /// Returns null when the price is not available.
    /// </summary>
    public async Task<decimal?> GetCardMarketPriceAsync(string scryfallId, bool foil, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scryfallId)) return null;
        try
        {
            var json = await ApiGetAsync($"cards/{scryfallId}", ct);
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            return ExtractPrice(doc.RootElement, foil);
        }
        catch { return null; }
    }

    private static decimal? ExtractPrice(JsonElement root, bool foil)
    {
        if (!root.TryGetProperty("prices", out var prices)) return null;
        var key = foil ? "usd_foil" : "usd";
        if (prices.TryGetProperty(key, out var priceEl))
        {
            var s = priceEl.GetString();
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
                return value;
        }
        // Fall back to the other variant if the requested one is null
        var fallback = foil ? "usd" : "usd_foil";
        if (prices.TryGetProperty(fallback, out var fallbackEl))
        {
            var s = fallbackEl.GetString();
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Fetches and stores market prices for a list of cards.
    /// <paramref name="onResult"/> is called with (card, price). When <paramref name="baselineOnly"/>
    /// is true only cards without a baseline are included (caller should pre-filter).
    /// </summary>
    public async Task BackfillCardPricesAsync(
        IReadOnlyList<Library.Models.Card> cards,
        Action<Library.Models.Card, decimal> onResult,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        int total = cards.Count;
        int done  = 0;

        foreach (var card in cards)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(card.ScryfallId)) { progress?.Report((++done, total)); continue; }

            try
            {
                var json = await ApiGetAsync($"cards/{card.ScryfallId}", ct);
                if (json != null)
                {
                    using var doc = JsonDocument.Parse(json);
                    var price = ExtractPrice(doc.RootElement, card.Foil);
                    if (price.HasValue) onResult(card, price.Value);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* skip on network error */ }

            progress?.Report((++done, total));
        }
    }
}

public class ScryfallCardData
{
    public string ScryfallId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SetCode { get; set; } = string.Empty;
    public string SetName { get; set; } = string.Empty;
    public string CollectorNumber { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    /// <summary>Comma-separated color identity, e.g. "W,U,B".</summary>
    public string ColorIdentity { get; set; } = string.Empty;
    /// <summary>Mana cost string, e.g. "{2}{U}{B}".</summary>
    public string ManaCost { get; set; } = string.Empty;
    /// <summary>Type line, e.g. "Legendary Creature — Human Wizard".</summary>
    public string TypeLine { get; set; } = string.Empty;
}

/// <summary>One page of Scryfall card search results, with optional next-page URL for pagination.</summary>
public record ScryfallPageResult(
    List<ScryfallCardData> Cards,
    bool HasMore,
    string? NextPageUrl);

/// <summary>Result of a Scryfall full-syntax search: the set of matching Scryfall IDs and card names.</summary>
public record ScryfallSearchResult(
    HashSet<string> ScryfallIds,
    HashSet<string> Names);

/// <summary>Oracle text and P/T or loyalty for the card detail panel.</summary>
public record CardTextData(
    string? OracleText,
    string? Power,
    string? Toughness,
    string? Loyalty,
    string? Defense);
