using Library.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Library.Services;

public static class DeckTextService
{
    // Standard export format (MTGA-compatible with header comments)
    //
    // // Name: My Deck
    // // Format: Commander
    //
    // Commander
    // 1 Atraxa, Praetors' Voice
    //
    // Deck
    // 1 Sol Ring
    // 24 Forest
    //
    // Sideboard
    // 2 Tormod's Crypt

    private static string CardLine(DeckCard dc)
    {
        var card = dc.Card!;
        var suffix = string.IsNullOrWhiteSpace(card.SetCode)
            ? ""
            : string.IsNullOrWhiteSpace(card.CollectorNumber)
                ? $" ({card.SetCode.ToUpperInvariant()})"
                : $" ({card.SetCode.ToUpperInvariant()}) {card.CollectorNumber}";
        return $"{dc.Quantity} {card.Name}{suffix}";
    }

    public static string Export(Deck deck)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Name: {deck.Name}");
        if (!string.IsNullOrWhiteSpace(deck.Format))
            sb.AppendLine($"// Format: {deck.Format}");
        sb.AppendLine();

        var commanders = deck.Cards.Where(c => c.IsCommander && c.Card != null).ToList();
        var main = deck.Cards.Where(c => !c.IsCommander && !c.IsSideboard && c.Card != null)
                             .OrderBy(c => c.Card!.Name).ToList();
        var side = deck.Cards.Where(c => c.IsSideboard && c.Card != null).ToList();

        if (commanders.Count > 0)
        {
            sb.AppendLine("Commander");
            foreach (var dc in commanders)
                sb.AppendLine(CardLine(dc));
            sb.AppendLine();
        }

        sb.AppendLine("Deck");
        foreach (var dc in main)
            sb.AppendLine(CardLine(dc));

        if (side.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Sideboard");
            foreach (var dc in side)
                sb.AppendLine(CardLine(dc));
        }

        return sb.ToString();
    }

    public record ParsedCard(int Quantity, string Name, bool IsCommander, bool IsSideboard, string? SetCode = null, string? CollectorNumber = null);

    public record ParseResult(
        string DeckName,
        string? Format,
        List<ParsedCard> Cards,
        List<string> ParseErrors);

    public static ParseResult Parse(string text)
    {
        var name = "Imported Deck";
        string? format = null;
        var cards = new List<ParsedCard>();
        var errors = new List<string>();
        var section = "Deck";
        var lineNum = 0;

        foreach (var rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNum++;
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Header comments
            if (line.StartsWith("//"))
            {
                var comment = line[2..].Trim();
                if (comment.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                    name = comment[5..].Trim();
                else if (comment.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                    format = comment[7..].Trim();
                continue;
            }

            // Section headers (with or without trailing colon)
            var header = line.TrimEnd(':');
            if (header.Equals("Commander", StringComparison.OrdinalIgnoreCase)) { section = "Commander"; continue; }
            if (header.Equals("Deck", StringComparison.OrdinalIgnoreCase)) { section = "Deck"; continue; }
            if (header.Equals("Sideboard", StringComparison.OrdinalIgnoreCase)) { section = "Sideboard"; continue; }

            // Card line: "4 Lightning Bolt" or "4x Lightning Bolt"
            var m = Regex.Match(line, @"^(\d+)x?\s+(.+)$");
            if (!m.Success)
            {
                errors.Add($"Line {lineNum}: unrecognized format \"{line}\"");
                continue;
            }

            var qty = int.Parse(m.Groups[1].Value);
            var rest = m.Groups[2].Value.Trim();

            // Extract trailing set/collector annotations like "(SNC) 123" or "(SNC)"
            string? setCode = null;
            string? collectorNumber = null;
            var setMatch = Regex.Match(rest, @"\s*\(([A-Z0-9]{2,5})\)\s*(\d+[a-z]?)?\s*$");
            if (setMatch.Success)
            {
                setCode = setMatch.Groups[1].Value;
                if (setMatch.Groups[2].Success)
                    collectorNumber = setMatch.Groups[2].Value;
                rest = rest[..setMatch.Index].Trim();
            }
            var cardName = rest;

            cards.Add(new ParsedCard(qty, cardName,
                IsCommander: section == "Commander",
                IsSideboard: section == "Sideboard",
                SetCode: setCode,
                CollectorNumber: collectorNumber));
        }

        return new ParseResult(name, format, cards, errors);
    }

    public static string SuggestedFileName(string deckName)
    {
        var safe = string.Concat(deckName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return safe.Trim() + ".txt";
    }
}
