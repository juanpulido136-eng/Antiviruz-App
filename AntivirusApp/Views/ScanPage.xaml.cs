using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AntivirusApp.Engine;
using AntivirusApp.Models;
using AntivirusApp.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AntivirusApp.Views;

public sealed partial class ScanPage : Page
{
    private const long MaxZipEntryBytes = 100L * 1024 * 1024;
    private static readonly ScanOptions QuickOptions = new(ScanMode.Quick);
    private static readonly ScanOptions FolderOptions = new(ScanMode.Folder, IncludeHidden: true);

    private readonly List<ScanItem> _all = new();
    private readonly ConcurrentQueue<ScanItem> _inbox = new();
    private readonly Stopwatch _clock = new();
    private readonly List<double> _speed = new();
    private DispatcherQueueTimer? _pump;
    private CancellationTokenSource? _cts;
    private Storyboard? _spin;
    private string _filter = "all";
    private string? _failure;
    private int _manualPercent = -1;
    private int _done, _clean, _sus, _mal, _err, _total;

    public ObservableCollection<ScanItem> Visible { get; } = new();

    public ScanPage()
    {
        InitializeComponent();
        SyncChips();
        Update();
    }

    private async void ScanFile_Click(object sender, RoutedEventArgs e)
    {
        StorageFile? file;

        try
        {
            var picker = Prepare(new FileOpenPicker());
            picker.FileTypeFilter.Add("*");
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {

            App.Log("ScanPage.ScanFile: selector FALLÓ", ex);
            HeadTitle.Text = "No se pudo abrir el selector";
            HeadDetail.Text = ex.GetType().Name + ": " + ex.Message;
            return;
        }

        if (file is null) return;

        var path = file.Path;
        var name = file.Name;
        var cts = Begin("Analizando archivo", name, 1);
        var progress = new Progress<int>(p =>
        {

            _manualPercent = p;
            Ring.Set(RingFill, p);
            RingPercent.Text = $"{p}%";
        });

        try
        {
            var result = await Task.Run(() => HeuristicScanner.SafeScan(path, QuickOptions, progress));
            Record(ScanItem.From(result, name, path, Path.GetDirectoryName(path) ?? "", false, SafeLength(path)));
        }
        catch (Exception ex)
        {
            App.Log("ScanFile FALLÓ", ex);
            _failure = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            Finish(cts);
        }
    }

    private async void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        StorageFolder? folder;

        try
        {
            var picker = Prepare(new FolderPicker());
            picker.FileTypeFilter.Add("*");
            folder = await picker.PickSingleFolderAsync();
        }
        catch (Exception ex)
        {
            App.Log("ScanPage.ScanFolder: selector FALLÓ", ex);
            HeadTitle.Text = "No se pudo abrir el selector";
            HeadDetail.Text = ex.GetType().Name + ": " + ex.Message;
            return;
        }

        if (folder is null) return;

        var cts = Begin("Analizando carpeta", folder.Path, 0);
        try
        {
            await ScanFolderAsync(folder.Path, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException)
        {
        }
        catch (Exception ex)
        {
            App.Log("ScanFolder FALLÓ", ex);
            _failure = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            Finish(cts);
        }
    }

    private async Task ScanFolderAsync(string folder, CancellationToken ct)
    {
        var files = await Task.Run(() => FileEnumerator.Enumerate(folder, FolderOptions, ct).ToList(), ct);
        _total = files.Count;
        Update();

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
            CancellationToken = ct
        };

        await Task.Run(() => Parallel.ForEach(files, parallel, path =>
        {
            var result = HeuristicScanner.SafeScan(path, FolderOptions);
            _inbox.Enqueue(ScanItem.From(result, Path.GetRelativePath(folder, path), path,
                Path.GetDirectoryName(path) ?? folder, false, SafeLength(path)));
        }), ct);
    }

    private async void ScanZip_Click(object sender, RoutedEventArgs e)
    {
        StorageFile? zipFile;

        try
        {
            var picker = Prepare(new FileOpenPicker());
            picker.FileTypeFilter.Add(".zip");
            zipFile = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            App.Log("ScanPage.ScanZip: selector FALLÓ", ex);
            HeadTitle.Text = "No se pudo abrir el selector";
            HeadDetail.Text = ex.GetType().Name + ": " + ex.Message;
            return;
        }

        if (zipFile is null) return;

        var cts = Begin("Analizando ZIP", zipFile.Name, 0);
        try
        {
            await ScanZipAsync(zipFile.Path, cts.Token);
        }
        catch (InvalidDataException)
        {
            _failure = "El archivo seleccionado no es un ZIP válido o está dañado.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.Log("ScanZip FALLÓ", ex);
            _failure = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            Finish(cts);
        }
    }

    private async Task ScanZipAsync(string zipPath, CancellationToken ct)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "AegisZip", Guid.NewGuid().ToString("N"));
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entries = zip.Entries
                .Where(en => !string.IsNullOrEmpty(en.Name) && en.Length <= MaxZipEntryBytes)
                .ToList();
            _total = entries.Count;
            Update();

            await Task.Run(() =>
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var entry = entries[i];

                    string temp;

                    try
                    {
                        var dir = Path.Combine(tempRoot, i.ToString());
                        Directory.CreateDirectory(dir);
                        temp = Path.Combine(dir, SafeFileName(entry.Name));
                        entry.ExtractToFile(temp, true);
                    }
                    catch (Exception ex)
                    {
                        App.Log("ScanZip: entrada omitida " + entry.FullName, ex);
                        _inbox.Enqueue(ScanItem.From(
                            new ScanResult
                            {
                                Verdict = Verdicts.Clean,
                                Reasons = new[] { "No se pudo extraer del ZIP: " + ex.Message },
                                IsError = true,
                                Size = entry.Length
                            },
                            entry.FullName, zipPath, zipPath, true, entry.Length));
                        continue;
                    }

                    try
                    {
                        var result = HeuristicScanner.SafeScan(temp, FolderOptions);
                        _inbox.Enqueue(ScanItem.From(result, entry.FullName, zipPath, zipPath, true, entry.Length));
                    }
                    catch (Exception ex)
                    {
                        App.Log("ScanZip: análisis falló en " + entry.FullName, ex);
                        _inbox.Enqueue(ScanItem.From(
                            new ScanResult
                            {
                                Verdict = Verdicts.Clean,
                                Reasons = new[] { "No se pudo analizar la entrada: " + ex.Message },
                                IsError = true,
                                Size = entry.Length
                            },
                            entry.FullName, zipPath, zipPath, true, entry.Length));
                    }
                    finally
                    {
                        try { File.Delete(temp); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }, ct);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string SafeFileName(string name)
    {
        try
        {
            var file = Path.GetFileName(name);

            if (string.IsNullOrWhiteSpace(file)) file = "entrada";

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                file = file.Replace(invalid, '_');
            }

            if (file.Length > 120) file = file[^120..];

            return file;
        }
        catch (Exception)
        {
            return "entrada";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        HeadDetail.Text = "Cancelando análisis";
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter = (string)((Button)sender).Tag;
        Rebuild();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultList.SelectedItem is not ScanItem item) return;
        ResultList.SelectedItem = null;
        ShellPage.Instance?.OpenDetail(item);
    }

    private CancellationTokenSource Begin(string title, string detail, int total)
    {
        var cts = new CancellationTokenSource();
        _cts = cts;

        foreach (var item in _all) item.Removed -= Forget;
        _all.Clear();
        Visible.Clear();
        _speed.Clear();
        _done = _clean = _sus = _mal = _err = 0;
        _total = total;
        _failure = null;
        _manualPercent = -1;
        while (_inbox.TryDequeue(out _)) { }

        _clock.Restart();
        StartSpin();
        StartPump();
        HeadTitle.Text = title;
        HeadDetail.Text = detail;
        RingLabel.Text = "preparando";
        SetBusy(true);
        Update();
        return cts;
    }

    private void Finish(CancellationTokenSource cts)
    {
        StopPump();
        StopSpin();
        _clock.Stop();

        bool cancelled = cts.IsCancellationRequested;
        int alerts = _sus + _mal;
        _manualPercent = -1;

        if (_done > _total) _total = _done;

        Update();

        if (!string.IsNullOrWhiteSpace(_failure))
        {
            HeadTitle.Text = "No se pudo completar el análisis";
            HeadDetail.Text = _failure;
        }
        else
        {
            HeadTitle.Text = cancelled ? "Análisis cancelado"
                : alerts == 0 ? "Sin amenazas detectadas"
                : $"{alerts} archivos requieren revisión";

            HeadDetail.Text = $"{_done} de {_total} archivos revisados" +
                              (_err > 0 ? $"  ·  {_err} sin acceso o ilegibles" : string.Empty);
        }

        RingLabel.Text = cancelled ? "cancelado" : _failure is null ? "completado" : "con avisos";

        string glyph = _mal > 0 ? "\uE783" : _sus > 0 ? "\uE7BA" : cancelled ? "\uE711" : "\uE73E";
        string key = _mal > 0 ? "DangerBrush" : _sus > 0 ? "WarnBrush" : cancelled ? "MutedBrush" : "OkBrush";

        if (_failure is null)
        {
            NotificationService.Summary(glyph, "Análisis completado", HeadTitle.Text,
                $"{_mal} maliciosos · {_sus} sospechosos · {_err} ilegibles · {_clean} limpios de {_done} archivos revisados",
                key);
        }
        else
        {
            NotificationService.Summary("\uE783", "Análisis incompleto", HeadTitle.Text, _failure, "DangerBrush");
        }

        _cts = null;
        cts.Dispose();
        SetBusy(false);
    }

    private void StartSpin()
    {
        try
        {
            RingSpin.Visibility = Visibility.Visible;

            var storyboard = new Storyboard();
            var spin = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(7)),
                RepeatBehavior = RepeatBehavior.Forever
            };

            Storyboard.SetTarget(spin, RingSpinTransform);
            Storyboard.SetTargetProperty(spin, "Angle");
            storyboard.Children.Add(spin);
            storyboard.Begin();

            _spin = storyboard;
        }
        catch (Exception ex)
        {
            App.Log("ScanPage.StartSpin FALLÓ", ex);
        }
    }

    private void StopSpin()
    {
        try
        {
            _spin?.Stop();
            _spin = null;
            RingSpin.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {

        }
    }

    private void StartPump()
    {
        StopPump();

        _pump = DispatcherQueue.CreateTimer();
        _pump.Interval = TimeSpan.FromMilliseconds(120);
        _pump.Tick += (_, _) => Drain();
        _pump.Start();
    }

    private void StopPump()
    {
        if (_pump is not null)
        {
            _pump.Stop();
            _pump = null;
        }

        while (_inbox.TryDequeue(out var item)) Record(item);
    }

    private void Drain()
    {
        int n = 0;
        while (n < 400 && _inbox.TryDequeue(out var item))
        {
            Record(item);
            n++;
        }
        Update();
    }

    private void Record(ScanItem item)
    {
        _done++;
        if (item.Verdict == Verdicts.Clean) _clean++;
        else if (item.Verdict == Verdicts.Suspicious) _sus++;
        else if (item.Verdict == Verdicts.Malicious) _mal++;
        else if (item.Verdict == Verdicts.Error) _err++;

        item.Removed += Forget;
        _all.Add(item);
        if (Matches(item)) Visible.Insert(0, item);

        App.Log($"scan {item.Name} -> {item.VerdictLabel} {item.RiskText}/100");

    }

    private void Forget(ScanItem item)
    {
        if (!_all.Remove(item)) return;
        Visible.Remove(item);
        _done--;
        if (item.Verdict == Verdicts.Clean) _clean--;
        else if (item.Verdict == Verdicts.Suspicious) _sus--;
        else if (item.Verdict == Verdicts.Malicious) _mal--;
        else if (item.Verdict == Verdicts.Error) _err--;
        Update();
    }

    private void Rebuild()
    {
        Visible.Clear();
        foreach (var item in _all.AsEnumerable().Reverse())
        {
            if (Matches(item)) Visible.Add(item);
        }
        SyncChips();
        Update();
    }

    private bool Matches(ScanItem item)
    {
        bool filterOk = _filter switch
        {
            "M" => item.Verdict == Verdicts.Malicious,
            "S" => item.Verdict == Verdicts.Suspicious,
            _ => true
        };
        if (!filterOk) return false;

        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;

        return item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.Origin.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.VerdictLabel.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Update()
    {
        double pct = _manualPercent >= 0
            ? Math.Clamp(_manualPercent, 0, 100)
            : _total == 0 ? 0 : Math.Min(100.0, _done * 100.0 / _total);
        Ring.Set(RingFill, pct);
        RingFill.Stroke = RingColor();
        RingPercent.Text = $"{(int)pct}%";
        if (_manualPercent >= 0)
        {
            RingLabel.Text = "analizando";
        }
        else if (_total > 0)
        {
            RingLabel.Text = $"{_done} de {_total}";
        }

        KTotal.Text = _done.ToString();
        KClean.Text = _clean.ToString();
        KSus.Text = _sus.ToString();
        KMal.Text = _mal.ToString();

        DistGrid.ColumnDefinitions[0].Width = new GridLength(_clean, GridUnitType.Star);
        DistGrid.ColumnDefinitions[1].Width = new GridLength(_sus, GridUnitType.Star);
        DistGrid.ColumnDefinitions[2].Width = new GridLength(_mal, GridUnitType.Star);

        double secs = _clock.Elapsed.TotalSeconds;
        if (secs > 0.5)
        {
            double rate = _done / secs;
            SpeedText.Text = $"{rate:0.0} archivos por segundo";
            _speed.Add(rate);
            if (_speed.Count > 80) _speed.RemoveAt(0);
            DrawSpark();
        }
        else
        {
            SpeedText.Text = "Calculando velocidad";
        }

        CountText.Text = $"{Visible.Count} de {_all.Count} archivos";
    }

    private void DrawSpark()
    {
        var points = new PointCollection();
        double max = _speed.Count == 0 ? 1 : Math.Max(_speed.Max(), 0.0001);
        for (int i = 0; i < _speed.Count; i++)
        {
            double x = _speed.Count == 1 ? 0 : i * 600.0 / (_speed.Count - 1);
            double y = 46 - _speed[i] / max * 40;
            points.Add(new Windows.Foundation.Point(x, y));
        }
        Spark.Points = points;
    }

    private Brush RingColor() =>
        _mal > 0 ? Palette.Of("DangerBrush")
        : _sus > 0 ? Palette.Of("WarnBrush")
        : _done > 0 ? Palette.Of("OkBrush")
        : Palette.Of("AccentBrush");

    private void SyncChips()
    {
        Mark(ChipAll, _filter == "all");
        Mark(ChipMal, _filter == "M");
        Mark(ChipSus, _filter == "S");
    }

    private static void Mark(Button button, bool on)
    {
        button.Background = on ? Palette.Of("RaisedBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Foreground = on ? Palette.Of("TextBrush") : Palette.Of("MutedBrush");
    }

    private void SetBusy(bool busy)
    {
        FileButton.IsEnabled = !busy;
        FolderButton.IsEnabled = !busy;
        ZipButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static T Prepare<T>(T picker) where T : class
    {
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        return picker;
    }
}
