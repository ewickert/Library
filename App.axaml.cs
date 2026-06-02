using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Library.Services;
using Library.ViewModels;
using Library.Views;
using System.Threading.Tasks;

namespace Library;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Register the theme slot before creating the window so resources are
        // available when controls are first built.
        Library.Services.ThemeService.Instance.Initialize(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var db = new DatabaseService();
            var scryfall = new ScryfallService();
            var vm = new MainWindowViewModel(db, scryfall);
            var mainView = new MainView
            {
                DataContext = vm
            };

            // On mobile we currently don't show the printing picker window.
            // Use the tapped result directly so "Add" and "Want" actions still work.
            ScryfallResultViewModel.GlobalPickPrintingAsync = data => Task.FromResult<ScryfallCardData?>(data);

            vm.RequestImportCsv = async () =>
            {
                void SetImportStatus(string message, bool isError)
                {
                    vm.ImportStatus = message;
                    vm.ImportStatusIsError = isError;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000);
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (vm.ImportStatus == message)
                                vm.ImportStatus = string.Empty;
                        });
                    });
                }

                var topLevel = TopLevel.GetTopLevel(mainView);
                if (topLevel?.StorageProvider is null)
                {
                    SetImportStatus("Import unavailable on this screen.", true);
                    return;
                }

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Cards from CSV",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("CSV Files") { Patterns = ["*.csv", "*.txt"] }]
                });

                if (files.Count == 0)
                {
                    SetImportStatus("Import cancelled.", false);
                    return;
                }

                var path = files[0].TryGetLocalPath();
                if (path is null)
                {
                    SetImportStatus("Could not access selected file.", true);
                    return;
                }

                var importer = new CsvImportService();
                var result = importer.Import(path);
                if (result.HasFatalError)
                {
                    SetImportStatus($"Import failed: {result.Error}", true);
                    return;
                }

                foreach (var card in result.Cards)
                    db.AddCard(card);

                vm.Collection.LoadCards();

                var summary = $"Imported {result.ImportedCount} card(s).";
                if (result.RowErrors.Count > 0)
                    summary += $" {result.RowErrors.Count} row(s) skipped.";

                SetImportStatus(summary, false);
            };

            singleView.MainView = mainView;
        }

        Library.Services.ThemeService.Instance.LoadSaved();

        base.OnFrameworkInitializationCompleted();
    }
}