using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AntivirusApp.Controls;
using AntivirusApp.Models;
using AntivirusApp.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AntivirusApp.Views;

public sealed partial class SpacePage : Page
{
    private List<AppUsage> _apps = new();
    private List<FolderUsage> _folders = new();
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _appsBars;
    private bool _foldersBars;
    private string _zipRoot = string.Empty;

    public SpacePage()
    {
        InitializeComponent();

        _zipRoot = DefaultZipRoot();
        ZipRootText.Text = _zipRoot;

        MeasureButton.Click += async (_, _) => await MeasureAppsAsync();
        ScanFoldersButton.Click += async (_, _) => await ScanFoldersAsync();
        PickZipFolderButton.Click += async (_, _) => await PickZipFolderAsync();
        AppViewButton.Click += (_, _) => ToggleAppsView();
        FolderViewButton.Click += (_, _) => ToggleFoldersView();
        TreemapHost.SizeChanged += (_, _) => DrawTreemap();
        FolderMapHost.SizeChanged += (_, _) => DrawFolderMap();

        SetBusy(false);
        BuildAppRows();
        BuildFolderRows();
        DrawTreemap();
        DrawFolderMap();
        Loaded += (_, _) =>
        {
            Helpers.Anim.FadeIn(PageRoot);
            Helpers.Anim.FadeIn(HeroCard, 40);
            Helpers.Anim.FadeIn(KpiApps, 90);
            Helpers.Anim.FadeIn(KpiAppsSize, 140);
            Helpers.Anim.FadeIn(KpiFolders, 190);
            Helpers.Anim.FadeIn(KpiFoldersSize, 240);
        };
    }

    private static string DefaultZipRoot()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile) && Directory.Exists(profile)) return profile;

        return SpaceService.DefaultRoots().FirstOrDefault() ?? "C:\\";
    }

    private async Task PickZipFolderAsync()
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.CurrentWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            _zipRoot = folder.Path;
            ZipRootText.Text = _zipRoot;
            HeadDetail.Text = "Carpeta elegida para ZIP: " + _zipRoot + ". Pulsa Analizar carpetas.";
        }
        catch (Exception ex)
        {
            App.Log("SpacePage.PickZipFolder FALLÓ", ex);
            HeadDetail.Text = "No se pudo abrir el selector: " + ex.Message;
        }
    }

    private async Task MeasureAppsAsync()
    {
        if (_busy) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var counters = new CleanerCounters { Running = true, Current = "apps" };

        SetBusy(true);
        Bar.IsIndeterminate = true;
        HeadDetail.Text = "Midiendo aplicaciones instaladas…";

        try
        {
            var apps = await Task.Run(() => CleanerService.MeasureApps(counters, token), token);

            _apps = apps;

            string summary = _apps.Count == 0
                ? "No se encontraron aplicaciones medibles."
                : $"{_apps.Count} aplicaciones · {CleanerService.FormatSize(_apps.Sum(a => a.Size))} en total.";

            NotificationService.Summary("\uE71D", "Aegiss_TheClurer", "Aplicaciones medidas", summary, "AccentBrush");

            BuildAppRows();
            DrawTreemap();

            HeadDetail.Text = summary;
        }
        catch (OperationCanceledException)
        {
            HeadDetail.Text = "Medición detenida.";
        }
        catch (Exception ex)
        {
            App.Log("SpacePage.MeasureApps FALLÓ", ex);
            HeadDetail.Text = "No se pudo medir: " + ex.Message;
        }
        finally
        {
            Bar.IsIndeterminate = false;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void BuildAppRows()
    {
        AppHost.Children.Clear();
        AppsCount.Text = _apps.Count == 0 ? "Sin datos" : $"{_apps.Count} aplicaciones";

        KApps.Text = _apps.Count.ToString("N0");
        KAppsSize.Text = CleanerService.FormatSize(_apps.Sum(a => a.Size));

        if (_apps.Count == 0)
        {
            AppHost.Children.Add(Note("Sin datos: pulsa Medir aplicaciones."));
            return;
        }

        foreach (var app in _apps.Take(200))
        {
            AppHost.Children.Add(Row(
                "\uE71D",
                app.Name,
                $"{app.Group}  ·  {app.Files:N0} archivos  ·  {app.Path}",
                CleanerService.FormatSize(app.Size),
                new[]
                {
                    ("Mostrar carpeta", false, (Action)(() => CleanerService.Reveal(app.Path)))
                }));
        }
    }

    private void ToggleAppsView()
    {
        _appsBars = !_appsBars;

        TreemapHost.Visibility = _appsBars ? Visibility.Collapsed : Visibility.Visible;
        AppBarsScroll.Visibility = _appsBars ? Visibility.Visible : Visibility.Collapsed;

        AppViewText.Text = _appsBars ? "Ver mapa de cuadros" : "Gráfica de barras animadas";
        AppDescText.Text = _appsBars
            ? "Cada barra es una aplicación: cuanto más larga, más pesa. Las barras crecen al dibujarse."
            : "Cada cuadrado es una aplicación: más grande, más pesa. Los pequeños son apps ligeras.";

        if (_appsBars)
        {
            DrawAppBars();
        }
        else
        {
            DrawTreemap();
        }
    }

    private void ToggleFoldersView()
    {
        _foldersBars = !_foldersBars;

        FolderMapHost.Visibility = _foldersBars ? Visibility.Collapsed : Visibility.Visible;
        FolderBarsScroll.Visibility = _foldersBars ? Visibility.Visible : Visibility.Collapsed;

        FolderViewText.Text = _foldersBars ? "Ver mapa de cuadros" : "Gráfica de barras animadas";
        FolderDescText.Text = _foldersBars
            ? "Cada barra es una carpeta: cuanto más larga, más pesa. Las barras crecen al dibujarse."
            : "Cada cuadrado es una carpeta: más grande, más pesa. Estas son las candidatas a comprimir en ZIP.";

        if (_foldersBars)
        {
            DrawFolderBars();
        }
        else
        {
            DrawFolderMap();
        }
    }

    private void DrawAppBars()
    {
        var slices = new List<ChartSlice>();

        foreach (var app in _apps.OrderByDescending(a => a.Size).Take(12))
        {
            slices.Add(new ChartSlice
            {
                Label = app.Name,
                Value = app.Size,
                ValueText = CleanerService.FormatSize(app.Size),
                Color = Charts.ColorAt(slices.Count)
            });
        }

        Charts.Bars(AppBarsHost, slices, animate: true);
    }

    private void DrawFolderBars()
    {
        var slices = new List<ChartSlice>();

        foreach (var folder in _folders.OrderByDescending(f => f.Size).Take(12))
        {
            slices.Add(new ChartSlice
            {
                Label = folder.Name,
                Value = folder.Size,
                ValueText = CleanerService.FormatSize(folder.Size),
                Color = Charts.ColorAt(slices.Count)
            });
        }

        Charts.Bars(FolderBarsHost, slices, animate: true);
    }

    private void DrawTreemap()
    {
        if (TreemapHost is null) return;

        double width = TreemapHost.ActualWidth;
        if (width < 120) width = 900;
        const double height = 300;

        var items = new List<TreemapItem>();

        foreach (var app in _apps.Take(12))
        {
            items.Add(new TreemapItem
            {
                Tag = "APP",
                Label = app.Name,
                Sub = $"{CleanerService.FormatSize(app.Size)}  ·  {app.Files:N0} archivos",
                Value = app.Size,
                Color = Charts.ColorAt(items.Count)
            });
        }

        Treemap.Draw(TreemapHost, items, width, height);

        TreemapNote.Text = _apps.Count == 0
            ? "Sin medir todavía. Pulsa Medir aplicaciones."
            : $"Las {Math.Min(12, _apps.Count)} más pesadas de {_apps.Count}  ·  total {CleanerService.FormatSize(_apps.Sum(a => a.Size))}";

        if (_appsBars)
        {
            DrawAppBars();
        }
    }

    private void DrawFolderMap()
    {
        if (FolderMapHost is null) return;

        double width = FolderMapHost.ActualWidth;
        if (width < 120) width = 900;
        const double height = 240;

        var items = new List<TreemapItem>();

        foreach (var folder in _folders.Take(12))
        {
            items.Add(new TreemapItem
            {
                Tag = "ZIP",
                Label = folder.Name,
                Sub = $"{CleanerService.FormatSize(folder.Size)}  ·  {folder.Files:N0} archivos",
                Value = folder.Size,
                Color = Charts.ColorAt(items.Count)
            });
        }

        Treemap.Draw(FolderMapHost, items, width, height);

        FolderMapNote.Text = _folders.Count == 0
            ? "Sin analizar todavía. Pulsa Analizar carpetas."
            : $"Las {Math.Min(12, _folders.Count)} carpetas más pesadas de {_folders.Count}  ·  total {CleanerService.FormatSize(_folders.Sum(f => f.Size))}";

        if (_foldersBars)
        {
            DrawFolderBars();
        }
    }

    private async Task ScanFoldersAsync()
    {
        if (_busy) return;

        string root = _zipRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            root = DefaultZipRoot();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var counters = new SpaceCounters { Current = "carpetas" };

        SetBusy(true);
        Bar.IsIndeterminate = true;
        HeadDetail.Text = "Analizando carpetas de " + root + " …";

        try
        {
            var report = await Task.Run(() => SpaceService.Scan(root, counters, token), token);

            _folders = report.Folders;

            NotificationService.Summary("\uE8B7", "Aegiss_TheClurer", "Carpetas analizadas",
                $"{_folders.Count} carpetas · {SpaceService.FormatSize(report.Total)} · {root}", "AccentBrush");

            BuildFolderRows();
            DrawFolderMap();

            HeadDetail.Text = $"{_folders.Count} carpetas  ·  {SpaceService.FormatSize(report.Total)}  ·  {root}";
        }
        catch (OperationCanceledException)
        {
            HeadDetail.Text = "Análisis detenido.";
        }
        catch (Exception ex)
        {
            App.Log("SpacePage.ScanFolders FALLÓ", ex);
            HeadDetail.Text = "No se pudo analizar: " + ex.Message;
        }
        finally
        {
            Bar.IsIndeterminate = false;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void BuildFolderRows()
    {
        FolderHost.Children.Clear();
        FoldersCount.Text = _folders.Count == 0 ? "Sin datos" : $"{_folders.Count} carpetas";

        KFolders.Text = _folders.Count.ToString("N0");
        KFoldersSize.Text = CleanerService.FormatSize(_folders.Sum(f => f.Size));

        if (_folders.Count == 0)
        {
            FolderHost.Children.Add(Note("Sin datos: pulsa Analizar carpetas para ZIP."));
            return;
        }

        foreach (var folder in _folders.Take(200))
        {
            var captured = folder;

            var actions = new List<(string Text, bool Danger, Action Click)>
            {
                ("Comprimir", false, () => _ = CompressAsync(captured, false))
            };

            if (!folder.Protected)
                actions.Add(("Comprimir y borrar", true, () => _ = CompressAsync(captured, true)));

            FolderHost.Children.Add(Row(
                "\uE8B7",
                folder.Name + (folder.Protected ? "  ·  protegida" : string.Empty),
                $"{folder.Files:N0} archivos  ·  {folder.Path}",
                CleanerService.FormatSize(folder.Size),
                actions));
        }
    }

    private async Task CompressAsync(FolderUsage folder, bool deleteOriginal)
    {
        if (_busy) return;

        if (folder.Protected)
        {
            HeadDetail.Text = "Esa carpeta está protegida: no se comprime ni se borra.";
            return;
        }

        var choice = await ConfirmAsync(folder, deleteOriginal);
        if (choice is null) return;
        bool delete = choice.Value;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        SetBusy(true);
        Bar.IsIndeterminate = true;
        HeadDetail.Text = "Comprimiendo " + folder.Name + " …";

        try
        {
            var progress = new Progress<string>(message => HeadDetail.Text = folder.Name + "  ·  " + message);

            var outcome = await Task.Run(
                () => SpaceService.Compress(folder.Path, delete, progress, token),
                token);

            ShowOutcome(outcome);

            if (outcome.Ok && outcome.Deleted)
            {
                _folders.Remove(folder);
                BuildFolderRows();
            }

            HeadDetail.Text = outcome.Ok
                ? $"{folder.Name}: {CleanerService.FormatSize(outcome.OriginalBytes)} → " +
                  $"{CleanerService.FormatSize(outcome.ZipBytes)}" +
                  (outcome.Deleted ? "  ·  original borrado" : "  ·  original conservado")
                : "No se pudo comprimir: " + (outcome.Error ?? "error desconocido");
        }
        catch (OperationCanceledException)
        {
            HeadDetail.Text = "Compresión cancelada.";
        }
        catch (Exception ex)
        {
            App.Log("SpacePage.Compress FALLÓ", ex);
            HeadDetail.Text = "Error al comprimir: " + ex.Message;
        }
        finally
        {
            Bar.IsIndeterminate = false;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<bool?> ConfirmAsync(FolderUsage folder, bool startWithDelete)
    {
        var content = new StackPanel { Spacing = 12 };

        content.Children.Add(new TextBlock
        {
            Text = "Se comprimirá esta carpeta en un ZIP dentro de:",
            FontSize = 12,
            Foreground = Palette.Of("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new Border
        {
            Background = Palette.Of("RaisedBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = SpaceService.ArchivesDir,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            }
        });

        content.Children.Add(new TextBlock
        {
            Text = folder.Path + "   (" + CleanerService.FormatSize(folder.Size) + ")",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });

        var warning = new TextBlock
        {
            Text = "La carpeta original se borrará de forma definitiva. El ZIP se verifica antes de borrar: " +
                   "si algo no cuadra, la carpeta queda intacta.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Palette.Of("WarnBrush")
        };

        var confirm = new CheckBox
        {
            Content = "Entiendo que la carpeta original se borrará",
            IsChecked = false
        };

        content.Children.Add(warning);
        content.Children.Add(confirm);

        var dialog = new ContentDialog
        {
            Title = startWithDelete ? "Comprimir y borrar original" : "Comprimir a ZIP",
            Content = content,
            PrimaryButtonText = startWithDelete ? "Comprimir y borrar" : "Comprimir",
            SecondaryButtonText = startWithDelete ? "Solo comprimir" : string.Empty,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = !startWithDelete,
            XamlRoot = XamlRoot
        };

        confirm.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        confirm.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        confirm.Visibility = startWithDelete ? Visibility.Visible : Visibility.Collapsed;
        warning.Visibility = startWithDelete ? Visibility.Visible : Visibility.Collapsed;

        var result = await dialog.ShowAsync();

        return result switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => (bool?)null
        };
    }

    private void ShowOutcome(ZipOutcome outcome)
    {
        string name = Path.GetFileName(outcome.Folder);
        bool ok = outcome.Ok;

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(new FontIcon
        {
            Glyph = ok ? "\uE73E" : "\uE783",
            FontSize = 12,
            Foreground = Palette.Of(ok ? "OkBrush" : "DangerBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        line.Children.Add(new TextBlock
        {
            Text = ok
                ? $"{name}: {CleanerService.FormatSize(outcome.OriginalBytes)} → {CleanerService.FormatSize(outcome.ZipBytes)}" +
                  (outcome.Deleted ? "  ·  ZIP verificado y original borrado" : "  ·  ZIP verificado, original conservado")
                : $"{name}: {outcome.Error ?? "error desconocido"}",
            FontSize = 12,
            Foreground = Palette.Of("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (ok)
            NotificationService.Summary("\uE7B8", "Aegiss_TheClurer", "ZIP creado",
                name + "  ·  " + CleanerService.FormatSize(outcome.ZipBytes), "OkBrush");
        else
            NotificationService.Summary("\uE783", "Aegiss_TheClurer", "No se comprimió",
                outcome.Error ?? "Error desconocido", "DangerBrush");

        try
        {
            ResultHost.Children.Add(line);
        }
        catch (Exception ex)
        {
            App.Log("SpacePage.ShowOutcome FALLÓ", ex);
        }
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = Palette.Of("MutedBrush"),
        Margin = new Thickness(16, 16, 16, 16),
        TextWrapping = TextWrapping.Wrap
    };

    private static Border Row(string glyph, string name, string sub, string size,
                              IEnumerable<(string Text, bool Danger, Action Click)> actions)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 9, 16, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = Palette.Of("RaisedBrush"),
            Child = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Palette.Of("MutedBrush") }
        };

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = sub,
            FontSize = 12,
            Foreground = Palette.Of("MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var sizeText = new TextBlock
        {
            Text = size,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

        foreach (var action in actions)
        {
            var button = new Button
            {
                Content = action.Text,
                Style = (Style)Application.Current.Resources[action.Danger ? "DangerGhostButton" : "GhostButton"],
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 12
            };

            var click = action.Click;
            button.Click += (_, _) => click();
            buttons.Children.Add(button);
        }

        Grid.SetColumn(texts, 1);
        Grid.SetColumn(sizeText, 2);
        Grid.SetColumn(buttons, 3);

        grid.Children.Add(icon);
        grid.Children.Add(texts);
        grid.Children.Add(sizeText);
        grid.Children.Add(buttons);

        return new Border
        {
            Child = grid,
            BorderBrush = Palette.Of("LineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        MeasureButton.IsEnabled = !busy;
        ScanFoldersButton.IsEnabled = !busy;
        PickZipFolderButton.IsEnabled = !busy;
    }
}
