using System.Text.RegularExpressions;
using Library.Models;

namespace Library.Services;

/// <summary>
/// Evaluates a Scryfall-like search query entirely against local card data — no network required.
///
/// Supported keywords (case-insensitive):
///   name / n        → card name (also the default for plain unqualified words)
///   t / type        → type line  (t:creature, t:legendary)
///   c / color       → card colour identity  (c:wu, c:m, c:c, c=wub)
///   id / identity   → commander identity, ≤ means "fits within"  (id:uw)
///   r / rarity      → rarity  (r:r, rarity>=uncommon)
///   s / set / e     → set code or name  (s:mkm, e:dom)
///   cmc / mv        → converted mana cost / mana value  (cmc>=5, mv=3)
///   foil            → foil flag  (foil:true, -foil:yes)
///   lang / language → language field  (lang:en)
///   condition       → condition field
///   q / qty         → quantity  (q>=4)
///
/// Operators: :  =  !=  &lt;  &lt;=  &gt;  &gt;=
/// Negation:  prefix token with -   (-t:creature)
/// Logic:     tokens are ANDed; separate groups with "or" for OR  (t:creature or t:planeswalker)
/// </summary>
public static class LocalCardFilter
{
    private sealed record Token(bool Negate, string Keyword, string Op, string Value);

    // ── Public entry point ────────────────────────────────────────────────────

    public static bool Matches(Card card, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        // Fast path: if the query contains no keyword operators, treat the
        // whole string as a plain-text search across name, set code, and set name
        // (backwards-compatible with old behaviour).  Keyword syntax still works
        // whenever at least one token contains an operator character.
        if (!ContainsKeywordSyntax(query))
        {
            return card.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || card.SetCode.Contains(query, StringComparison.OrdinalIgnoreCase)
                || card.SetName.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        var orGroups = ParseOrGroups(query);
        return orGroups.Any(group => group.All(token => MatchToken(card, token)));
    }

    // Returns true when the query looks like it uses keyword syntax (contains
    // at least one token with an operator: colon, =, <, >, !).
    private static readonly Regex _hasSyntaxRx = new(
        @"\w+[:<>=!]", RegexOptions.Compiled);

    private static bool ContainsKeywordSyntax(string query) =>
        _hasSyntaxRx.IsMatch(query);

    // ── Parsing ───────────────────────────────────────────────────────────────

    private static List<List<Token>> ParseOrGroups(string query)
    {
        var groups = new List<List<Token>>();
        var parts = Regex.Split(query.Trim(), @"\bor\b", RegexOptions.IgnoreCase);
        foreach (var part in parts)
        {
            var tokens = ParseTokens(part.Trim());
            if (tokens.Count > 0) groups.Add(tokens);
        }
        if (groups.Count == 0) groups.Add([]);
        return groups;
    }

    // Matches either "keyword op value" or a plain word (optionally quoted)
    private static readonly Regex _wordRx = new(
        @"""[^""]*""|\S+", RegexOptions.Compiled);
    private static readonly Regex _keywordRx = new(
        @"^(\w+)([:<>=!]+)(.+)$", RegexOptions.Compiled);

    private static List<Token> ParseTokens(string query)
    {
        var tokens = new List<Token>();
        foreach (Match m in _wordRx.Matches(query))
        {
            var raw = m.Value;

            // Strip leading negate dash before quotes or keywords
            bool negate = raw.StartsWith('-') && raw.Length > 1;
            if (negate) raw = raw[1..];

            // Strip surrounding quotes for plain values
            if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2)
                raw = raw[1..^1];

            if (raw.Equals("and", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("or",  StringComparison.OrdinalIgnoreCase))
                continue;

            var km = _keywordRx.Match(raw);
            if (km.Success)
            {
                tokens.Add(new Token(
                    negate,
                    km.Groups[1].Value.ToLowerInvariant(),
                    km.Groups[2].Value,
                    km.Groups[3].Value.Trim('"').ToLowerInvariant()));
            }
            else
            {
                tokens.Add(new Token(negate, "name", ":", raw.ToLowerInvariant()));
            }
        }
        return tokens;
    }

    // ── Token matching ────────────────────────────────────────────────────────

    private static bool MatchToken(Card card, Token t)
    {
        bool result = t.Keyword switch
        {
            "name" or "n"
                => card.Name.Contains(t.Value, StringComparison.OrdinalIgnoreCase),

            "t" or "type"
                => (card.TypeLine ?? "").Contains(t.Value, StringComparison.OrdinalIgnoreCase),

            "c" or "color" or "colour"
                => MatchColor(card.ColorIdentity ?? "", t.Op, t.Value),

            "id" or "identity" or "ci"
                => MatchIdentity(card.ColorIdentity ?? "", t.Op, t.Value),

            "r" or "rarity"
                => MatchRarity(card.Rarity ?? "", t.Op, t.Value),

            "s" or "set" or "e" or "expansion"
                => card.SetCode.Equals(t.Value, StringComparison.OrdinalIgnoreCase)
                   || card.SetName.Contains(t.Value, StringComparison.OrdinalIgnoreCase),

            "cmc" or "mv" or "manavalue"
                => MatchNumeric(CalcCmc(card.ManaCost), t.Op, t.Value),

            "foil"
                => MatchBool(card.Foil, t.Value),

            "lang" or "language"
                => (card.Language ?? "").Contains(t.Value, StringComparison.OrdinalIgnoreCase),

            "condition"
                => (card.Condition ?? "").Contains(t.Value, StringComparison.OrdinalIgnoreCase),

            "q" or "qty" or "quantity"
                => MatchNumeric(card.Quantity, t.Op, t.Value),

            // Unknown keyword → fall back to name search
            _ => card.Name.Contains(t.Value, StringComparison.OrdinalIgnoreCase)
        };

        return t.Negate ? !result : result;
    }

    // ── Colour matching ───────────────────────────────────────────────────────

    private static bool MatchColor(string colorIdentity, string op, string value)
    {
        if (value is "c" or "colorless")
            return string.IsNullOrEmpty(colorIdentity);

        if (value is "m" or "multicolor" or "multi")
            return SplitColors(colorIdentity).Count >= 2;

        var query = ParseColorLetters(value);
        if (op is "=" or "==")
        {
            var ci = SplitColors(colorIdentity);
            return ci.Count == query.Count
                && query.All(c => ci.Contains(c, StringComparer.OrdinalIgnoreCase));
        }
        // ":" / ">=" → card has at least these colours
        return query.All(c => colorIdentity.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchIdentity(string colorIdentity, string op, string value)
    {
        if (value is "c" or "colorless")
            return string.IsNullOrEmpty(colorIdentity);

        var query = ParseColorLetters(value);
        var ci    = SplitColors(colorIdentity);

        return op switch
        {
            // id<=uw  →  card's CI is a subset of the queried colours (fits in that commander deck)
            "<=" or ":" => ci.All(c => query.Contains(c, StringComparer.OrdinalIgnoreCase)),
            // id>=uw  →  card's CI contains at least those colours
            ">="        => query.All(c => ci.Contains(c, StringComparer.OrdinalIgnoreCase)),
            "=" or "==" => ci.Count == query.Count
                           && ci.All(c => query.Contains(c, StringComparer.OrdinalIgnoreCase)),
            // default: subset (same as <=)
            _           => ci.All(c => query.Contains(c, StringComparer.OrdinalIgnoreCase))
        };
    }

    private static List<string> SplitColors(string colorIdentity) =>
        colorIdentity.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Where(s => s.Length > 0)
                     .ToList();

    private static List<string> ParseColorLetters(string value)
    {
        // Handle full words like "white", "blue", etc.
        var wordMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = "W", ["blue"] = "U", ["black"] = "B",
            ["red"]   = "R", ["green"] = "G"
        };
        if (wordMap.TryGetValue(value, out var single)) return [single];

        return value.ToUpperInvariant()
                    .Where(c => "WUBRG".Contains(c))
                    .Select(c => c.ToString())
                    .Distinct()
                    .ToList();
    }

    // ── Rarity matching ───────────────────────────────────────────────────────

    private static readonly Dictionary<string, int> _rarityOrder =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["common"]     = 0, ["c"] = 0,
            ["uncommon"]   = 1, ["u"] = 1,
            ["rare"]       = 2, ["r"] = 2,
            ["mythic"]     = 3, ["m"] = 3, ["mythic rare"] = 3
        };

    private static bool MatchRarity(string rarity, string op, string value)
    {
        if (!_rarityOrder.TryGetValue(value, out int qv)) return false;
        if (!_rarityOrder.TryGetValue(rarity, out int sv)) return false;
        return op switch
        {
            "=" or "==" or ":" => sv == qv,
            ">"                => sv >  qv,
            ">="               => sv >= qv,
            "<"                => sv <  qv,
            "<="               => sv <= qv,
            "!=" or "!"        => sv != qv,
            _                  => sv == qv
        };
    }

    // ── Numeric matching ──────────────────────────────────────────────────────

    private static bool MatchNumeric(double actual, string op, string value)
    {
        if (!double.TryParse(value, out double qv)) return false;
        return op switch
        {
            "=" or "==" or ":" => actual == qv,
            ">"                => actual >  qv,
            ">="               => actual >= qv,
            "<"                => actual <  qv,
            "<="               => actual <= qv,
            "!=" or "!"        => actual != qv,
            _                  => actual == qv
        };
    }

    private static bool MatchBool(bool actual, string value) => value switch
    {
        "true"  or "yes" or "1" => actual,
        "false" or "no"  or "0" => !actual,
        _                       => actual   // bare "foil:" with no value → true
    };

    // ── CMC calculation ───────────────────────────────────────────────────────
    // Parses mana cost strings like "{2}{U}{B}", "{W/U}", "{X}{R}", etc.

    private static readonly Regex _manaSymbolRx = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    private static double CalcCmc(string? manaCost)
    {
        if (string.IsNullOrEmpty(manaCost)) return 0;
        double cmc = 0;
        foreach (Match m in _manaSymbolRx.Matches(manaCost))
        {
            var sym = m.Groups[1].Value;

            // Pure number: {2}, {15}, etc.
            if (double.TryParse(sym, out double n)) { cmc += n; continue; }

            // Variable: {X}, {Y}, {Z} → 0
            if (sym.Length == 1 && "XYZ".Contains(sym, StringComparison.OrdinalIgnoreCase))
                continue;

            // Hybrid: {W/U} → 1, {2/W} → 2
            if (sym.Contains('/'))
            {
                var parts = sym.Split('/');
                cmc += double.TryParse(parts[0], out double h) ? h : 1;
                continue;
            }

            // Single colour or snow/phyrexian pip → 1
            cmc += 1;
        }
        return cmc;
    }
}
