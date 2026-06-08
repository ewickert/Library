using CommunityToolkit.Mvvm.Input;
using Library.Models;
using System.Windows.Input;

namespace Library.ViewModels;

/// <summary>Pairs a <see cref="DeckCard"/> with its optional image slot for use in the sorted view.</summary>
public sealed class DeckCardSortedItem
{
    public DeckCard DeckCard { get; }
    public CardSlotViewModel? Slot { get; }

    /// <summary>True when the card's type line qualifies it as a commander (legendary creature or planeswalker).</summary>
    public bool IsCommanderEligible { get; }

    /// <summary>True when the card is physically owned (not a shopping-list placeholder).</summary>
    public bool IsOwned => DeckCard.Card?.IsPlaceholder == false;

    /// <summary>True when the card is a shopping-list placeholder (not yet owned).</summary>
    public bool IsWanted => DeckCard.Card?.IsPlaceholder == true;

    /// <summary>Removes this card from the deck.</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Promotes this card to commander. Null when the card is not commander-eligible.</summary>
    public ICommand? PromoteToCommanderCommand { get; }

    /// <summary>Opens the alternate printings gallery for this card.</summary>
    public ICommand? AlternatePrintingsCommand { get; }

    public DeckCardSortedItem(DeckCard dc, CardSlotViewModel? slot,
        Action<DeckCard> remove, Action<DeckCard>? promote, Action<DeckCard>? alternatePrintings = null)
    {
        DeckCard = dc;
        Slot = slot;

        var typeLine = dc.Card?.TypeLine ?? string.Empty;
        IsCommanderEligible =
            typeLine.Contains("Legendary", StringComparison.OrdinalIgnoreCase) &&
            (typeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase) ||
             typeLine.Contains("Planeswalker", StringComparison.OrdinalIgnoreCase));

        RemoveCommand = new RelayCommand(() => remove(dc));
        PromoteToCommanderCommand = IsCommanderEligible && promote != null
            ? new RelayCommand(() => promote(dc))
            : null;
        AlternatePrintingsCommand = alternatePrintings != null
            ? new RelayCommand(() => alternatePrintings(dc))
            : null;
    }
}

/// <summary>Represents one grouped column in the sorted deck view (e.g. "Creatures", "Lands").</summary>
public sealed class DeckCategoryViewModel
{
    public string Icon  { get; }
    public string Name  { get; }
    public IReadOnlyList<DeckCardSortedItem> Items { get; }

    /// <summary>Total card count (sum of quantities).</summary>
    public int Count { get; }

    /// <summary>Formatted count label, e.g. "(31)".</summary>
    public string CountLabel => $"({Count})";

    /// <summary>Formatted total market price, e.g. "– $28.65". Empty when no price data.</summary>
    public string PriceLabel { get; }

    public DeckCategoryViewModel(string icon, string name,
        IEnumerable<DeckCard> cards,
        IReadOnlyDictionary<int, CardSlotViewModel> slotById,
        Action<DeckCard> removeAction,
        Action<DeckCard>? promoteAction,
        Action<DeckCard>? alternatePrintingsAction = null)
    {
        Icon  = icon;
        Name  = name;

        var ordered = cards.OrderBy(dc => dc.Card?.Name).ToList();
        Items = ordered
            .Select(dc => new DeckCardSortedItem(
                dc,
                dc.Card != null && slotById.TryGetValue(dc.Card.Id, out var s) ? s : null,
                removeAction,
                promoteAction,
                alternatePrintingsAction))
            .ToList();

        Count = ordered.Sum(dc => dc.Quantity);
        var total = ordered.Sum(dc => (dc.Card?.CurrentMarketPrice ?? 0m) * dc.Quantity);
        PriceLabel = total > 0 ? $"– ${total:F2}" : string.Empty;
    }
}
