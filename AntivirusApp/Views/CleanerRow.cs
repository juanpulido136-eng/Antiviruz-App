using System;
using AntivirusApp.Models;
using AntivirusApp.Services;

namespace AntivirusApp.Views;

public sealed class CleanerRow
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Sub { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime Modified { get; set; }

    public bool IsFolder { get; set; }

    public string Kind { get; set; } = "otro";

    public int Files { get; set; }

    public string SizeText => CleanerService.FormatSize(Size);

    public string ModifiedText => Modified == DateTime.MinValue ? "Sin fecha" : Modified.ToString("dd/MM/yyyy");

    public string KindText => Kind switch
    {
        "carpeta" => "carpeta",
        "aplicacion" => "aplicación",
        "base de datos" => "base de datos",
        "cache" => "caché",
        "codigo" => "código",
        _ => Kind
    };

    public string FilesText => Files.ToString("N0");

    public string Glyph => GlyphOf(Kind);

    public static CleanerRow FromItem(CleanerItem item) => new()
    {
        Path = item.Path,
        Name = item.Name,
        Sub = item.IsFolder ? $"{item.Files:N0} archivos  ·  {item.Directory}" : item.Directory,
        Size = item.Size,
        Modified = item.Modified,
        IsFolder = item.IsFolder,
        Kind = item.IsFolder ? "carpeta" : item.Kind,
        Files = item.Files
    };

    public static CleanerRow FromApp(AppUsage app) => new()
    {
        Path = app.Path,
        Name = app.Name,
        Sub = app.Group + "  ·  " + app.Path,
        Size = app.Size,
        Modified = app.Newest,
        IsFolder = true,
        Kind = "aplicacion",
        Files = app.Files
    };

    private static string GlyphOf(string kind) => kind switch
    {
        "video" => "\uE714",
        "imagen" => "\uE91B",
        "audio" => "\uE8D6",
        "documento" => "\uE8A5",
        "comprimido" => "\uE7B8",
        "instalador" => "\uE896",
        "codigo" => "\uE943",
        "base de datos" => "\uE8F1",
        "cache" => "\uE81C",
        "carpeta" => "\uE8B7",
        "aplicacion" => "\uE71D",
        _ => "\uE7C3"
    };
}
