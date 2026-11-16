using System;
using AntivirusApp.Models;
using AntivirusApp.Services;

namespace AntivirusApp.Views;

public sealed class RootOption
{
    public string Path { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public override string ToString() => Label;

    public static RootOption From(string path) => new()
    {
        Path = path,
        Label = SpaceService.Label(path)
    };
}

public sealed class SpaceRow
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Sub { get; set; } = string.Empty;

    public long Size { get; set; }

    public int Files { get; set; }

    public double Percent { get; set; }

    public bool Protected { get; set; }

    public string SizeText => SpaceService.FormatSize(Size);

    public string PercentText => Percent.ToString("0.#") + " %";

    public string FilesText => Files.ToString("N0") + " archivos";

    public string Glyph => Protected ? "\uE785" : "\uE8B7";

    public string ProtectText => Protected ? "protegida" : string.Empty;

    public static SpaceRow From(FolderUsage folder, long total)
    {
        double percent = total > 0 ? folder.Size * 100.0 / total : 0;

        string sub = folder.Files.ToString("N0") + " archivos";
        if (folder.Modified > DateTime.MinValue)
            sub += "  ·  último cambio " + folder.Modified.ToString("dd/MM/yyyy");

        return new SpaceRow
        {
            Path = folder.Path,
            Name = folder.Name,
            Sub = sub,
            Size = folder.Size,
            Files = folder.Files,
            Percent = Math.Min(100, percent),
            Protected = folder.Protected
        };
    }
}
