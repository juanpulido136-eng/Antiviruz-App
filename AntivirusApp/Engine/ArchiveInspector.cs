using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AntivirusApp.Engine;

public static class ArchiveInspector
{
    private static readonly string[] ExecutableExtensions =
    {
        ".exe", ".dll", ".scr", ".com", ".pif", ".bat", ".cmd", ".ps1", ".vbs",
        ".js", ".jse", ".wsf", ".hta", ".lnk", ".msi", ".jar"
    };

    public static int Score(string path, List<string> reasons, int cap = 55)
    {
        int score = 0;

        void Add(int weight, string reason)
        {
            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }

            score = Math.Min(cap, score + weight);
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var entries = zip.Entries.Take(400).ToList();

            int executables = 0;
            long totalUncompressed = 0;

            foreach (var entry in entries)
            {
                string name = entry.FullName;

                if (name.Contains("..", StringComparison.Ordinal) ||
                    name.StartsWith("/", StringComparison.Ordinal) ||
                    name.StartsWith("\\\\", StringComparison.Ordinal) ||
                    name.Contains(":\\", StringComparison.Ordinal))
                {
                    Add(25, $"Entrada con ruta de escape ({name})");
                }

                string extension = Path.GetExtension(name);

                if (ExecutableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    executables++;
                }

                totalUncompressed += entry.Length;

                if (entry.Length > 0 && entry.CompressedLength > 0)
                {
                    double ratio = entry.Length / (double)entry.CompressedLength;

                    if (ratio > 200 && entry.Length > 10L * 1024 * 1024)
                    {
                        Add(25, $"Posible bomba de descompresión ({ratio:0}x en {name})");
                    }
                }
            }

            if (executables > 0)
            {
                Add(Math.Min(22, 12 + executables * 2),
                    $"El ZIP contiene {executables} archivo(s) ejecutable(s)");
            }

            if (totalUncompressed > 2L * 1024 * 1024 * 1024)
            {
                Add(15, $"El contenido descomprimido supera 2 GB ({totalUncompressed / (1024.0 * 1024 * 1024):0.#} GB)");
            }
        }
        catch (InvalidDataException)
        {
            Add(10, "ZIP dañado o con estructura no estándar");
        }
        catch (Exception)
        {

        }

        return score;
    }
}
