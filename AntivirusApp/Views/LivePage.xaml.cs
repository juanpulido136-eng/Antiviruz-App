using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AntivirusApp.Engine;
using AntivirusApp.Models;
using AntivirusApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AntivirusApp.Views;

public sealed partial class LivePage : Page
{
    private static readonly string Downloads = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private const int Capacity = 300;

    private readonly SemaphoreSlim _gate = new(1);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScanItem> _all = new();
    private readonly ScanOptions _opt = new(ScanMode.Automatic);
    private FolderWatcher? _watcher;
    private bool _updatingSwitch;
    private string? _current;
    private int _pending, _analyzed, _clean, _sus, _mal;

    public ObservableCollection<ScanItem> Visible { get; } = new();

    public LivePage()
    {
        InitializeComponent();
        PathText.Text = Downloads;
        Refresh();
    }

    private void Watch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingSwitch) return;

        if (WatchSwitch.IsOn)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_watcher is not null) return;

        try
        {
            _watcher = new FolderWatcher(Downloads);
            _watcher.FileDetected += path => DispatcherQueue.TryEnqueue(() => Enqueue(path));
            Refresh();

            NotificationService.Summary("\uE7C1", "Vigilancia en vivo", "Vigilancia activada",
                "Se revisará cada archivo nuevo de " + Downloads, "AccentBrush");
        }
        catch (Exception ex)
        {

            App.Log("LivePage.Start FALLÓ", ex);
            _watcher?.Dispose();
            _watcher = null;

            _updatingSwitch = true;
            WatchSwitch.IsOn = false;
            _updatingSwitch = false;

            CurrentText.Text = "No se pudo iniciar la vigilancia: " + ex.Message;
            Refresh();

            NotificationService.Summary("\uE783", "Vigilancia en vivo", "No se pudo activar",
                ex.Message, "DangerBrush");
        }
    }

    private void Stop()
    {
        try
        {
            _watcher?.Dispose();
        }
        catch (Exception ex)
        {
            App.Log("LivePage.Stop FALLÓ", ex);
        }

        _watcher = null;
        Refresh();

        NotificationService.Summary("\uE7C2", "Vigilancia en vivo", "Vigilancia detenida",
            "Ya no se revisan los archivos nuevos de Descargas.", "MutedBrush");
    }

    private void Enqueue(string path)
    {
        if (!_seen.Add(path)) return;
        _pending++;
        Refresh();
        _ = AnalyzeAsync(path);
    }

    private async Task AnalyzeAsync(string path)
    {
        await _gate.WaitAsync();

        if (_pending > 0) _pending--;

        _current = Path.GetFileName(path);
        Refresh();

        try
        {
            if (!await FolderWatcher.WaitUntilReadyAsync(path, TimeSpan.FromSeconds(60)))
            {
                _seen.Remove(path);
                return;
            }

            if (!File.Exists(path))
            {
                _seen.Remove(path);
                return;
            }

            var progress = new Progress<int>(p =>
            {
                Ring.Set(RingFill, p);
                RingPercent.Text = $"{p}%";
                RingLabel.Text = "analizando";
            });

            var result = await Task.Run(() => HeuristicScanner.SafeScan(path, _opt, progress));
            Record(ScanItem.From(result, Path.GetFileName(path), path,
                Path.GetDirectoryName(path) ?? Downloads, false, SafeLength(path)));
        }
        finally
        {
            _current = null;
            _gate.Release();
            Refresh();
        }
    }

    private void Record(ScanItem item)
    {
        item.Removed += Forget;
        _all.Insert(0, item);
        Visible.Insert(0, item);

        if (_all.Count > Capacity)
        {
            var old = _all[^1];
            _all.RemoveAt(_all.Count - 1);
            Visible.Remove(old);
        }

        _analyzed++;
        if (item.Verdict == Verdicts.Clean) _clean++;
        else if (item.Verdict == Verdicts.Suspicious) _sus++;
        else if (item.Verdict == Verdicts.Malicious) _mal++;

        if (item.IsAlert) NotificationService.Alert(item);
        Refresh();
    }

    private void Forget(ScanItem item)
    {
        if (!_all.Remove(item)) return;
        Visible.Remove(item);
        _analyzed--;
        if (item.Verdict == Verdicts.Clean) _clean--;
        else if (item.Verdict == Verdicts.Suspicious) _sus--;
        else if (item.Verdict == Verdicts.Malicious) _mal--;
        Refresh();
    }

    private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultList.SelectedItem is not ScanItem item) return;
        ResultList.SelectedItem = null;
        ShellPage.Instance?.OpenDetail(item);
    }

    private void Refresh()
    {
        bool watching = _watcher is not null;

        StateText.Text = _current is not null ? "Analizando archivo"
            : watching ? "Vigilando Descargas"
            : "Vigilancia detenida";

        CurrentText.Text = _current ?? (!watching ? "Activa la vigilancia para empezar"
            : _pending > 0 ? $"{_pending} archivos en cola"
            : "Esperando archivos nuevos");

        QueueText.Text = $"{_pending} en cola";

        if (_current is null)
        {
            Ring.Set(RingFill, 0);
            RingPercent.Text = "0%";
            RingLabel.Text = watching ? "en espera" : "detenida";
        }

        KAnalyzed.Text = _analyzed.ToString();
        KClean.Text = _clean.ToString();
        KSus.Text = _sus.ToString();
        KMal.Text = _mal.ToString();
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
}
