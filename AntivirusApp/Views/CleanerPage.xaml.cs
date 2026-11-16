using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AntivirusApp.Controls;
using AntivirusApp.Models;
using AntivirusApp.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AntivirusApp.Views;

public sealed partial class CleanerPage : Page
{
    private enum Tab { Files, Folders }

    private readonly List<string> _roots = new();

    private CleanerScanResult _scan = new();
    private CleanerCounters _counters = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueueTimer? _pump;

    private Tab _tab = Tab.Files;
    private bool _busy;
    private string? _folder;
    private List<string>? _lastRoots;
    private string _lastWhat = string.Empty;

    private Storyboard? _orbitOuter;
    private Storyboard? _orbitInner;
    private Storyboard? _satellite;
    private Storyboard? _halo;
    private Storyboard? _badgeBeat;
    private Storyboard? _dotA;
    private Storyboard? _dotB;
    private Storyboard? _dotC;

    private long _shownFiles = -1;
    private long _shownDirs = -1;
    private long _shownBytes = -1;

    public CleanerPage()
    {
        InitializeComponent();

        _roots.AddRange(PcRoots());

        ScanButton.Click += async (_, _) => await ScanAsync(PcRoots(), "el equipo completo");
        PickFolderButton.Click += async (_, _) => await PickFolderAsync();
        SmallToggle.Toggled += async (_, _) => await OnFilterChangedAsync();
        ScanFolderButton.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_folder)) return;
            await ScanAsync(new List<string> { _folder }, "la carpeta " + System.IO.Path.GetFileName(_folder));
        };
        StopButton.Click += (_, _) =>
        {
            _cts?.Cancel();
            HeadDetail.Text = "Deteniendo el escaneo…";
        };

        TabFiles.Click += (_, _) => { _tab = Tab.Files; Helpers.Anim.Pop(TabFiles); Refresh(); };
        TabFolders.Click += (_, _) => { _tab = Tab.Folders; Helpers.Anim.Pop(TabFolders); Refresh(); };

        SetBusy(false);
        UpdateFilterText();
        Loaded += (_, _) =>
        {
            Helpers.Anim.FadeIn(PageRoot);

            int card = 0;
            foreach (var child in PageRoot.Children.OfType<FrameworkElement>())
            {
                Helpers.Anim.SlideIn(child, 40 + card * 45, 14);
                card++;
            }

            StartHeroMotion();
            Refresh();
        };
        Refresh();

        HeadDetail.Text = "Escanear equipo completo revisa: " + string.Join("  ·  ", _roots) +
                          "   (no entra en Windows, System32, WinSxS ni Program Files)";
    }

    private static List<string> PcRoots()
    {
        var list = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        list.Add(drive.RootDirectory.FullName);
                }
                catch (Exception)
                {

                }
            }
        }
        catch (Exception ex)
        {
            App.Log("CleanerPage.PcRoots FALLÓ", ex);
        }

        if (list.Count == 0) list.AddRange(CleanerService.DefaultRoots());
        return list;
    }

    private async Task ScanAsync(List<string> roots, string what)
    {
        if (_busy) return;

        if (roots.Count == 0)
        {
            HeadDetail.Text = "No hay ninguna ruta que analizar.";
            return;
        }

        bool small = SmallToggle.IsOn;

        var options = new CleanerOptions(roots.ToArray())
        {
            MinFileBytes = small ? 1L * 1024 * 1024 : 20L * 1024 * 1024,
            MinFolderBytes = small ? 5L * 1024 * 1024 : 60L * 1024 * 1024,
            SkipSystem = true,
            IncludeHidden = false,
            MaxFiles = small ? 2000 : 800
        };

        _lastRoots = roots;
        _lastWhat = what;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _counters = new CleanerCounters { Running = true, Current = "Preparando" };
        _scan = new CleanerScanResult();
        RowHost.Children.Clear();
        BarsHost.Children.Clear();

        SetBusy(true);
        Bar.IsIndeterminate = true;
        HeadDetail.Text = "Escaneando " + what + "… puede tardar unos minutos.";
        StartPump();

        try
        {
            var counters = _counters;
            var result = await Task.Run(() => CleanerService.Scan(options, counters, token), token);

            _scan = result;
            Refresh();

            string resumen =
                $"{counters.Files:N0} archivos revisados  ·  {counters.Dirs:N0} carpetas  ·  " +
                $"{CleanerService.FormatSize(counters.Bytes)} medidos  ·  {counters.Skipped:N0} carpetas de sistema omitidas";

            if (!string.IsNullOrWhiteSpace(counters.Error))
            {

                HeadDetail.Text = "El escaneo terminó con avisos: " + counters.Error;
                NotificationService.Summary("\uE783", "Aegiss_Clenaer", "Escaneo incompleto",
                    counters.Error, "DangerBrush");
            }
            else
            {
                HeadDetail.Text = resumen + "   ·   " + what;

                NotificationService.Summary("\uE721", "Aegiss_Clenaer", "Escaneo completado",
                    $"{result.Files.Count} archivos y {result.Folders.Count} carpetas superan el mínimo.",
                    "AccentBrush");
            }

            if (counters.Errors > 0)
            {
                App.Log($"CleanerPage: {counters.Errors} rutas ilegibles omitidas");
            }
        }
        catch (OperationCanceledException)
        {
            HeadDetail.Text = "Escaneo detenido. No se ha modificado nada.";
        }
        catch (Exception ex)
        {
            App.Log("CleanerPage.Scan FALLÓ", ex);
            HeadDetail.Text = "No se pudo completar el escaneo: " + ex.Message;
        }
        finally
        {
            StopPump();
            Bar.IsIndeterminate = false;
            Bar.Value = 0;
            SyncCounters();
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void UpdateFilterText()
    {
        FilterText.Text = SmallToggle.IsOn
            ? "Modo detallado: se miden archivos desde 1 MB y carpetas desde 5 MB."
            : "Solo se miden los elementos grandes: archivos de 20 MB o más y carpetas de 60 MB o más.";
    }

    private async Task OnFilterChangedAsync()
    {
        UpdateFilterText();

        if (_busy || _lastRoots is null || _lastRoots.Count == 0) return;

        await ScanAsync(_lastRoots, string.IsNullOrWhiteSpace(_lastWhat) ? "el equipo completo" : _lastWhat);
    }

    private async Task PickFolderAsync()
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.CurrentWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            _folder = folder.Path;
            FolderPathText.Text = folder.Path;
            ScanFolderButton.IsEnabled = !_busy;
            HeadDetail.Text = "Carpeta elegida. Pulsa Analizar esta carpeta para medir solo esa ruta.";
        }
        catch (Exception ex)
        {
            App.Log("CleanerPage.PickFolder FALLÓ", ex);
            HeadDetail.Text = "No se pudo abrir el selector de carpetas: " + ex.Message;
        }
    }

    private List<CleanerItem> CurrentItems() =>
        _tab == Tab.Files ? _scan.Files : _scan.Folders;

    private void Refresh()
    {
        var items = CurrentItems();

        CountText.Text = items.Count == 0 ? "Sin datos" : $"{items.Count} elementos";
        ChartTitle.Text = _tab == Tab.Files
            ? "TOP DE LOS ARCHIVOS QUE MÁS PESAN"
            : "TOP DE LAS CARPETAS QUE MÁS PESAN";

        ListHint.Text = _scan.Files.Count == 0 && _scan.Folders.Count == 0
            ? "Sin resultados todavía: pulsa Escanear equipo completo."
            : _tab == Tab.Files
                ? "Archivos más pesados encontrados"
                : "Carpetas más pesadas encontradas";

        MarkTabs();
        BuildRows(items);
        DrawBars(items);
    }

    private void MarkTabs()
    {
        TabFiles.Background = _tab == Tab.Files ? Palette.Of("RaisedBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        TabFiles.Foreground = _tab == Tab.Files ? Palette.Of("TextBrush") : Palette.Of("MutedBrush");

        TabFolders.Background = _tab == Tab.Folders ? Palette.Of("RaisedBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        TabFolders.Foreground = _tab == Tab.Folders ? Palette.Of("TextBrush") : Palette.Of("MutedBrush");
    }

    private void DrawBars(List<CleanerItem> items)
    {
        var slices = new List<ChartSlice>();

        foreach (var item in items.Where(i => i.Size > 0).Take(8))
        {
            slices.Add(new ChartSlice
            {
                Label = item.Name,
                Value = item.Size,
                Color = Charts.ColorAt(slices.Count),
                ValueText = CleanerService.FormatSize(item.Size)
            });
        }

        Charts.Bars(BarsHost, slices, animate: true);
    }

    private void BuildRows(List<CleanerItem> items)
    {
        RowHost.Children.Clear();

        if (items.Count == 0)
        {
            RowHost.Children.Add(new TextBlock
            {
                Text = "Nada que mostrar aquí todavía.",
                FontSize = 13,
                Foreground = Palette.Of("MutedBrush"),
                Margin = new Thickness(16, 18, 16, 18)
            });
            return;
        }

        int index = 0;
        foreach (var item in items.Take(200))
        {
            var row = BuildRow(item);
            RowHost.Children.Add(row);

            if (index < 14)
            {
                Helpers.Anim.SlideIn(row, index * 26, 12);
            }
            else
            {
                row.Opacity = 1;
            }

            index++;
        }
    }

    private Border BuildRow(CleanerItem item)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 9, 16, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = Palette.Of("RaisedBrush"),
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            Child = new FontIcon
            {
                Glyph = item.IsFolder ? "\uE8B7" : "\uE8A5",
                FontSize = 14,
                Foreground = Palette.Of("MutedBrush")
            }
        };

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = item.IsFolder && item.Files > 0
                ? $"{item.Files:N0} archivos  ·  {item.Directory}"
                : item.Directory,
            FontSize = 12,
            Foreground = Palette.Of("MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var size = new TextBlock
        {
            Text = CleanerService.FormatSize(item.Size),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var show = new Button
        {
            Content = "Mostrar carpeta",
            Style = (Style)Application.Current.Resources["GhostButton"],
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var path = item.Path;
        show.Click += (_, _) => CleanerService.Reveal(path);

        var delete = new Button
        {
            Content = "Eliminar",
            Style = (Style)Application.Current.Resources["DangerGhostButton"],
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var captured = item;
        delete.Click += async (_, _) => await ConfirmDeleteAsync(captured);

        Grid.SetColumn(texts, 1);
        Grid.SetColumn(size, 2);
        Grid.SetColumn(show, 3);
        Grid.SetColumn(delete, 4);

        grid.Children.Add(icon);
        grid.Children.Add(texts);
        grid.Children.Add(size);
        grid.Children.Add(show);
        grid.Children.Add(delete);

        var container = new Border
        {
            Child = grid,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = Palette.Of("LineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        container.PointerEntered += (_, _) =>
        {
            container.Background = Palette.Of("RaisedBrush");
            Helpers.Anim.Pulse(icon, 1.12, 200);
        };
        container.PointerExited += (_, _) =>
        {
            container.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        };

        return container;
    }

    private async Task ConfirmDeleteAsync(CleanerItem item)
    {
        var content = new StackPanel { Spacing = 12 };

        content.Children.Add(new TextBlock
        {
            Text = "Vas a eliminar " + (item.IsFolder ? "esta carpeta" : "este archivo") + ":",
            FontSize = 12,
            Foreground = Palette.Of("MutedBrush")
        });

        content.Children.Add(new Border
        {
            Background = Palette.Of("RaisedBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = item.Path,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            }
        });

        content.Children.Add(new TextBlock
        {
            Text = "ADVERTENCIA: elimínalo solo si no lo vas a utilizar. " +
                   "Si crees que puedes arrepentirte, pulsa Cancelar y no se tocará nada.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Palette.Of("WarnBrush")
        });

        content.Children.Add(new TextBlock
        {
            Text = $"Se enviará a la Papelera de reciclaje · {CleanerService.FormatSize(item.Size)}",
            FontSize = 12,
            Foreground = Palette.Of("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        var confirm = new CheckBox { Content = "Confirmo que no lo voy a utilizar", IsChecked = false };
        content.Children.Add(confirm);

        var dialog = new ContentDialog
        {
            Title = "Eliminar " + (item.IsFolder ? "carpeta" : "archivo"),
            Content = content,
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot
        };

        confirm.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        confirm.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var result = CleanerService.MoveToTrash(new[] { item.Path });

            if (result.Ok)
            {

                _scan.Files.RemoveAll(i => IsSameOrInside(i.Path, item.Path));
                _scan.Folders.RemoveAll(i => IsSameOrInside(i.Path, item.Path));
                Refresh();

                NotificationService.Summary("\uE73E", "Aegiss_Clenaer", "Enviado a la papelera",
                    item.Name + "  ·  " + CleanerService.FormatSize(item.Size), "OkBrush");
            }
            else
            {
                NotificationService.Summary("\uE783", "Aegiss_Clenaer", "No se pudo eliminar",
                    result.Failed.FirstOrDefault() ?? "Puede estar en uso.", "DangerBrush");
            }
        }
        catch (Exception ex)
        {
            App.Log("CleanerPage.ConfirmDelete FALLÓ", ex);
            NotificationService.Summary("\uE783", "Aegiss_Clenaer", "Error al eliminar", ex.Message, "DangerBrush");
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ScanButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
        TabFiles.IsEnabled = !busy;
        TabFolders.IsEnabled = !busy;
        PickFolderButton.IsEnabled = !busy;
        ScanFolderButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_folder);
        SmallToggle.IsEnabled = !busy;

        SweepBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (IsLoaded) StartHeroMotion();
    }

    private void StartHeroMotion()
    {
        StopHeroMotion();

        bool fast = _busy;

        _halo = Helpers.Anim.Breathe(HeroHalo, 0.28, 0.85, fast ? 1.3 : 2.8);
        _badgeBeat = Helpers.Anim.PulseLoop(HeroBadge, 1.05, fast ? 1.0 : 2.2);
        _orbitOuter = Helpers.Anim.Spin(HeroOrbitTransform, fast ? 4.5 : 16);
        _orbitInner = Helpers.Anim.Spin(HeroInnerTransform, fast ? 3.2 : 11, reverse: true);
        _satellite = Helpers.Anim.Spin(HeroSatelliteTransform, fast ? 2.0 : 7);

        _dotA = Helpers.Anim.Breathe(DotFiles, 0.35, 1.0, fast ? 0.9 : 1.8);
        _dotB = Helpers.Anim.Breathe(DotDirs, 0.35, 1.0, fast ? 0.9 : 1.8);
        _dotC = Helpers.Anim.Breathe(DotBytes, 0.35, 1.0, fast ? 0.9 : 1.8);
    }

    private void StopHeroMotion()
    {
        _orbitOuter?.Stop();
        _orbitInner?.Stop();
        _satellite?.Stop();
        _halo?.Stop();
        _badgeBeat?.Stop();
        _dotA?.Stop();
        _dotB?.Stop();
        _dotC?.Stop();

        _orbitOuter = null;
        _orbitInner = null;
        _satellite = null;
        _halo = null;
        _badgeBeat = null;
        _dotA = null;
        _dotB = null;
        _dotC = null;
    }

    private static bool IsSameOrInside(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;

        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void StartPump()
    {
        _pump = DispatcherQueue.CreateTimer();
        _pump.Interval = TimeSpan.FromMilliseconds(120);
        _pump.Tick += (_, _) => SyncCounters();
        _pump.Start();
    }

    private void StopPump()
    {
        if (_pump is null) return;
        _pump.Stop();
        _pump = null;
    }

    private void SyncCounters()
    {
        long files = _counters.Files;
        long dirs = _counters.Dirs;
        long bytes = _counters.Bytes;

        if (files != _shownFiles)
        {
            _shownFiles = files;
            KFiles.Text = files.ToString("N0");
            if (files > 0) Helpers.Anim.Pulse(KFiles, 1.08, 220);
        }

        if (dirs != _shownDirs)
        {
            _shownDirs = dirs;
            KDirs.Text = dirs.ToString("N0");
            if (dirs > 0) Helpers.Anim.Pulse(KDirs, 1.08, 220);
        }

        if (bytes != _shownBytes)
        {
            _shownBytes = bytes;
            KBytes.Text = CleanerService.FormatSize(bytes);
            if (bytes > 0) Helpers.Anim.Pulse(KBytes, 1.06, 220);
        }

        if (_busy && _counters.Current.Length > 0)
            HeadDetail.Text = "Escaneando… " + _counters.Current;
    }
}
