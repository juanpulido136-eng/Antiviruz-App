using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AntivirusApp.Models;
using Microsoft.UI.Xaml.Media;

namespace AntivirusApp.Views;

public static class Palette
{
    public static Brush Of(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];

    public static string KeyFor(string verdict) => verdict switch
    {
        Verdicts.Malicious => "DangerBrush",
        Verdicts.Suspicious => "WarnBrush",
        Verdicts.Clean => "OkBrush",
        _ => "MutedBrush"
    };

    public static string GlyphFor(string verdict) => verdict switch
    {
        Verdicts.Malicious => "\uE783",
        Verdicts.Suspicious => "\uE7BA",
        Verdicts.Clean => "\uE73E",
        _ => "\uE783"
    };

    public static string LabelFor(string verdict) => verdict switch
    {
        Verdicts.Malicious => "Malicioso",
        Verdicts.Suspicious => "Sospechoso",
        Verdicts.Clean => "Limpio",
        _ => "Error"
    };
}

public sealed class ScanItem
{
    private ScanItem(string name, string filePath, string origin, bool archiveEntry, ScanResult result, long size, DateTime when)
    {
        Name = name;
        FilePath = filePath;
        Origin = origin;
        IsArchiveEntry = archiveEntry;
        Result = result;
        Size = size;
        When = when;
    }

    public static ScanItem From(ScanResult result, string name, string filePath, string origin, bool archiveEntry, long size) =>
        new(name, filePath, origin, archiveEntry, result, size, DateTime.Now);

    public event Action<ScanItem>? Removed;

    public string Name { get; }
    public string FilePath { get; }
    public string Origin { get; }
    public bool IsArchiveEntry { get; }
    public ScanResult Result { get; }
    public long Size { get; }
    public DateTime When { get; }

    public string Verdict => Result.IsError ? Verdicts.Error : Result.Verdict;

    public bool IsAlert => Verdict == Verdicts.Malicious || Verdict == Verdicts.Suspicious;
    public bool CanDelete => !IsArchiveEntry && !Result.IsError;
    public string VerdictGlyph => Palette.GlyphFor(Verdict);
    public string VerdictLabel => Palette.LabelFor(Verdict);
    public Brush Accent => Palette.Of(Palette.KeyFor(Verdict));
    public string TypeGlyph => IsArchiveEntry ? "\uE7B8" : "\uE8A5";
    public string RiskText => Result.IsError ? "N/D" : Result.RiskScore.ToString();
    public string SizeText => FormatSize(Size);
    public string TimeText => When.ToString("HH:mm:ss");
    public string TypeText => string.IsNullOrEmpty(Path.GetExtension(Name))
        ? "Sin extensión"
        : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
    public IReadOnlyList<string> Reasons => Result.Reasons;

    public void ShowInFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ShowInFolder: " + ex.Message);
        }
    }

    public void Remove()
    {
        if (!CanDelete) return;
        File.Delete(FilePath);
        Removed?.Invoke(this);
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} KB"
        : $"{bytes / (1024.0 * 1024.0):0.00} MB";
}
