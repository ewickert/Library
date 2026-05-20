using Library.Models;

namespace Library.Services;

public class CsvImportService
{
    // Maps known header variations to canonical property names
    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "Name",
        ["card name"] = "Name",
        ["cardname"] = "Name",

        ["set code"] = "SetCode",
        ["setcode"] = "SetCode",
        ["set"] = "SetCode",
        ["edition"] = "SetCode",

        ["set name"] = "SetName",
        ["setname"] = "SetName",
        ["edition name"] = "SetName",

        ["collector number"] = "CollectorNumber",
        ["collectornumber"] = "CollectorNumber",
        ["card number"] = "CollectorNumber",
        ["number"] = "CollectorNumber",

        ["foil"] = "Foil",
        ["is foil"] = "Foil",
        ["isfoil"] = "Foil",

        ["rarity"] = "Rarity",

        ["quantity"] = "Quantity",
        ["qty"] = "Quantity",
        ["count"] = "Quantity",

        ["manabox id"] = "ManaBoxId",
        ["manaboxid"] = "ManaBoxId",
        ["manabox_id"] = "ManaBoxId",

        ["scryfall id"] = "ScryfallId",
        ["scryfallid"] = "ScryfallId",
        ["scryfall_id"] = "ScryfallId",

        ["purchase price"] = "PurchasePrice",
        ["purchaseprice"] = "PurchasePrice",
        ["price"] = "PurchasePrice",
        ["cost"] = "PurchasePrice",

        ["misprint"] = "Misprint",
        ["is misprint"] = "Misprint",

        ["altered"] = "Altered",
        ["is altered"] = "Altered",

        ["condition"] = "Condition",
        ["grade"] = "Condition",

        ["language"] = "Language",
        ["lang"] = "Language",

        ["purchase price currency"] = "PurchasePriceCurrency",
        ["currency"] = "PurchasePriceCurrency",
        ["price currency"] = "PurchasePriceCurrency",

        ["added"] = "Added",
        ["date added"] = "Added",
        ["dateadded"] = "Added",
        ["acquired"] = "Added",
    };

    public CsvImportResult Import(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
            return new CsvImportResult { Error = "File is empty." };

        // Find the header row (skip blank/comment lines)
        int headerIndex = -1;
        for (int i = 0; i < Math.Min(lines.Length, 10); i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]) && !lines[i].TrimStart().StartsWith('#'))
            {
                headerIndex = i;
                break;
            }
        }
        if (headerIndex < 0)
            return new CsvImportResult { Error = "Could not find a header row." };

        var headers = ParseCsvLine(lines[headerIndex]);
        var columnMap = BuildColumnMap(headers);

        var cards = new List<Card>();
        var errors = new List<string>();

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var fields = ParseCsvLine(line);
            try
            {
                var card = ParseCard(fields, columnMap, headers.Count);
                cards.Add(card);
            }
            catch (Exception ex)
            {
                errors.Add($"Row {i + 1}: {ex.Message}");
            }
        }

        return new CsvImportResult { Cards = cards, RowErrors = errors };
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim();
            if (HeaderAliases.TryGetValue(header, out var canonical))
                map.TryAdd(canonical, i);
            else
                map.TryAdd(header, i); // keep raw header too
        }
        return map;
    }

    private static Card ParseCard(List<string> fields, Dictionary<string, int> map, int headerCount)
    {
        string Get(string col)
        {
            if (!map.TryGetValue(col, out var idx)) return string.Empty;
            return idx < fields.Count ? fields[idx].Trim() : string.Empty;
        }

        var name = Get("Name");
        if (string.IsNullOrWhiteSpace(name))
            throw new FormatException("Name is required.");

        return new Card
        {
            Name = name,
            SetCode = Get("SetCode"),
            SetName = Get("SetName"),
            CollectorNumber = Get("CollectorNumber"),
            Foil = ParseBool(Get("Foil")),
            Rarity = Get("Rarity"),
            Quantity = ParseInt(Get("Quantity"), 1),
            ManaBoxId = ParseLongNullable(Get("ManaBoxId")),
            ScryfallId = NullIfEmpty(Get("ScryfallId")),
            PurchasePrice = ParseDecimalNullable(Get("PurchasePrice")),
            Misprint = ParseBool(Get("Misprint")),
            Altered = ParseBool(Get("Altered")),
            Condition = DefaultIfEmpty(Get("Condition"), "NM"),
            Language = DefaultIfEmpty(Get("Language"), "en"),
            PurchasePriceCurrency = NullIfEmpty(Get("PurchasePriceCurrency")),
            Added = ParseDate(Get("Added")),
        };
    }

    // Handles quoted fields and escaped quotes per RFC 4180
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int pos = 0;
        while (pos <= line.Length)
        {
            if (pos == line.Length) { fields.Add(string.Empty); break; }

            if (line[pos] == '"')
            {
                pos++; // skip opening quote
                var sb = new System.Text.StringBuilder();
                while (pos < line.Length)
                {
                    if (line[pos] == '"')
                    {
                        if (pos + 1 < line.Length && line[pos + 1] == '"')
                        { sb.Append('"'); pos += 2; } // escaped quote
                        else { pos++; break; } // closing quote
                    }
                    else { sb.Append(line[pos++]); }
                }
                fields.Add(sb.ToString());
                if (pos < line.Length && line[pos] == ',') pos++;
            }
            else
            {
                int comma = line.IndexOf(',', pos);
                if (comma < 0) { fields.Add(line[pos..]); break; }
                fields.Add(line[pos..comma]);
                pos = comma + 1;
            }
        }
        return fields;
    }

    private static bool ParseBool(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        return s.Trim() is "1" or "true" or "yes" or "foil" or "✓"
            || string.Equals(s.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s, out var v) ? v : fallback;

    private static long? ParseLongNullable(string s)
        => long.TryParse(s, out var v) ? v : null;

    private static decimal? ParseDecimalNullable(string s)
        => decimal.TryParse(s, System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime ParseDate(string s)
        => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToUniversalTime()
            : DateTime.UtcNow;

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string DefaultIfEmpty(string s, string fallback)
        => string.IsNullOrWhiteSpace(s) ? fallback : s;
}

public class CsvImportResult
{
    public List<Card> Cards { get; set; } = new();
    public List<string> RowErrors { get; set; } = new();
    public string? Error { get; set; } // fatal error
    public bool HasFatalError => Error != null;
    public int ImportedCount => Cards.Count;
}
