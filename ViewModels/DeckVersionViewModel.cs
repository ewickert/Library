using CommunityToolkit.Mvvm.ComponentModel;
using Library.Models;

namespace Library.ViewModels;

public partial class DeckVersionViewModel : ObservableObject
{
    public DeckVersion Version { get; }

    [ObservableProperty] private bool _isSelectedA;
    [ObservableProperty] private bool _isSelectedB;

    public DeckVersionViewModel(DeckVersion version)
    {
        Version = version;
    }

    public string DisplayLabel => Version.DisplayLabel;
    public string ShortLabel => Version.ShortLabel;
    public bool IsAuto => Version.IsAuto;
    public string AutoBadge => Version.IsAuto ? " · auto" : string.Empty;
}
