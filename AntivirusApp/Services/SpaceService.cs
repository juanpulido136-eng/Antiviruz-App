using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using AntivirusApp.Models;

namespace AntivirusApp.Services;

public static class SpaceService
{
    private static readonly string[] SkipNames =
    {
        "$recycle.bin",
        "system volume information"
    };

    private static readonly string[] ProtectedNames =
    {
        "windows",
        "system32",
        "syswow64",
        "winsxs",
        "program files",
        "program files (x86)",
        "programdata",
        "recovery",
        "boot",
        "perflogs",
        "users",
        "$recycle.bin",
        "system volume information"
    };

    public static string ArchivesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aegis",
        "archives");

    public static List<string> DefaultRoots()
    {
        var roots = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;
                roots.Add(drive.RootDirectory.FullName);
            }
        }
        catch (Exception ex)
        {
            App.Log("SpaceService.DefaultRoots unidades FALLÓ", ex);
        }

        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                string path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !roots.Contains(path))
                    roots.Add(path);
            }
            catch (Exception)
            {

            }
        }

        Add(Environment.SpecialFolder.UserProfile);
        Add(Environment.SpecialFolder.MyVideos);
        Add(Environment.SpecialFolder.MyPictures);
        Add(Environment.SpecialFolder.MyMusic);
        Add(Environment.SpecialFolder.MyDocuments);

        return roots;
    }

    public static string Label(string path)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length <= 2) return "Unidad " + full + "\\";

            string name = Path.GetFileName(full);
            if (!string.IsNullOrEmpty(name))
            {
                return IsProtected(full) ? name + "  (protegida)" : name;
            }

            return full;
        }
        catch (Exception)
        {
            return path;
        }
    }

    public static SpaceReport Scan(string root, SpaceCounters counters, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var report = new SpaceReport { Root = root };

        var kinds = new Dictionary<string, KindUsage>(StringComparer.OrdinalIgnoreCase);

        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(root).ToList();
        }
        catch (Exception ex)
        {
            throw new IOException("No se pudo abrir " + root + ": " + ex.Message, ex);
        }

        foreach (var child in children)
        {
            token.ThrowIfCancellationRequested();

            string name = Path.GetFileName(child);
            if (string.IsNullOrWhiteSpace(name)) name = child;
            if (SkipNames.Contains(name.ToLowerInvariant())) continue;

            counters.Current = name;

            var usage = new FolderUsage
            {
                Path = child,
                Name = name,
                Protected = IsProtected(child)
            };

            MeasureFolder(child, usage, kinds, counters, token);

            report.Folders.Add(usage);
            counters.Folders++;
            counters.Bytes += usage.Size;
            counters.Files += usage.Files;
        }

        report.Folders.Sort((a, b) => b.Size.CompareTo(a.Size));
        report.Total = report.Folders.Sum(f => f.Size);
        report.Files = report.Folders.Sum(f => f.Files);

        report.Kinds.AddRange(kinds.Values.OrderByDescending(k => k.Size));

        report.Elapsed = watch.Elapsed;
        counters.Done = true;
        counters.Current = string.Empty;
        return report;
    }

    private static void MeasureFolder(
        string folder,
        FolderUsage usage,
        Dictionary<string, KindUsage> kinds,
        SpaceCounters counters,
        CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        try
        {
            var info = new DirectoryInfo(folder);
            if (!info.Exists) return;

            foreach (var file in info.EnumerateFiles("*", options))
            {
                token.ThrowIfCancellationRequested();

                long length;
                DateTime written;
                try
                {
                    length = file.Length;
                    written = file.LastWriteTime;
                }
                catch (Exception)
                {
                    continue;
                }

                usage.Size += length;
                usage.Files++;
                if (written > usage.Modified) usage.Modified = written;

                counters.Files++;
                counters.Bytes += length;

                string kind = CleanerService.KindOf(file.Extension);
                if (!kinds.TryGetValue(kind, out var entry))
                {
                    entry = new KindUsage { Kind = kind };
                    kinds[kind] = entry;
                }

                entry.Size += length;
                entry.Files++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            App.Log("SpaceService.MeasureFolder " + folder, ex);
        }
    }

    public static ZipOutcome Compress(
        string folder,
        bool deleteOriginal,
        IProgress<string>? progress,
        CancellationToken token)
    {
        var outcome = new ZipOutcome { Folder = folder };

        if (IsProtected(folder))
        {
            outcome.Error = "Carpeta protegida: la aplicación no comprime ni borra zonas del sistema.";
            return outcome;
        }

        if (!Directory.Exists(folder))
        {
            outcome.Error = "La carpeta ya no existe.";
            return outcome;
        }

        try
        {
            Directory.CreateDirectory(ArchivesDir);

            progress?.Report("Midiendo la carpeta…");
            var (bytes, files) = CountFiles(folder, token);
            outcome.OriginalBytes = bytes;
            outcome.Files = files;

            string name = SafeName(Path.GetFileName(folder.TrimEnd('\\')));
            if (name.Length == 0) name = "carpeta";

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string zipPath = Path.Combine(ArchivesDir, $"{name}-{stamp}.zip");
            int copy = 1;
            while (File.Exists(zipPath))
                zipPath = Path.Combine(ArchivesDir, $"{name}-{stamp}-{copy++}.zip");

            progress?.Report("Comprimiendo… esto puede tardar varios minutos");
            ZipFile.CreateFromDirectory(
                folder,
                zipPath,
                CompressionLevel.Optimal,
                includeBaseDirectory: true);

            token.ThrowIfCancellationRequested();

            outcome.ZipPath = zipPath;
            outcome.ZipBytes = new FileInfo(zipPath).Length;

            progress?.Report("Verificando el ZIP…");
            outcome.Verified = Verify(zipPath, files, bytes);

            if (!outcome.Verified)
            {
                TryDelete(zipPath);
                outcome.ZipPath = string.Empty;
                outcome.ZipBytes = 0;
                outcome.Error = "El ZIP no coincide con la carpeta original. Se borró el ZIP y la carpeta quedó intacta.";
                return outcome;
            }

            if (!deleteOriginal) return outcome;

            progress?.Report("Borrando la carpeta original…");
            try
            {
                Directory.Delete(folder, true);
                outcome.Deleted = true;
            }
            catch (Exception ex)
            {
                outcome.Error = "ZIP creado y verificado, pero no se pudo borrar la carpeta: " + ex.Message;
                App.Log("SpaceService.Delete " + folder, ex);
            }

            return outcome;
        }
        catch (OperationCanceledException)
        {
            outcome.Error = "Cancelado.";
            return outcome;
        }
        catch (Exception ex)
        {
            App.Log("SpaceService.Compress " + folder, ex);
            outcome.Error = ex.GetType().Name + ": " + ex.Message;
            return outcome;
        }
    }

    private static (long Bytes, int Files) CountFiles(string folder, CancellationToken token)
    {
        long bytes = 0;
        int files = 0;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                bytes += file.Length;
                files++;
            }
            catch (Exception)
            {

            }
        }

        return (bytes, files);
    }

    private static bool Verify(string zipPath, int expectedFiles, long expectedBytes)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            int files = 0;
            long bytes = 0;

            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                files++;
                bytes += entry.Length;
            }

            if (files != expectedFiles) return false;

            long minimum = (long)(expectedBytes * 0.995);
            return bytes >= minimum;
        }
        catch (Exception ex)
        {
            App.Log("SpaceService.Verify " + zipPath, ex);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            App.Log("SpaceService.TryDelete " + path, ex);
        }
    }

    public static bool IsProtected(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd('\\');
        }
        catch (Exception)
        {
            return true;
        }

        if (full.Length <= 3) return true;
        if (full.Length < 8) return true;

        string name = Path.GetFileName(full);
        if (name.Length == 0) return true;

        string lower = name.ToLowerInvariant();
        if (ProtectedNames.Contains(lower)) return true;

        string windows, profile, programData, programFiles, programFilesX86, archives;
        try
        {
            windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).TrimEnd('\\');
            programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
            programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
            archives = ArchivesDir.TrimEnd('\\');
        }
        catch (Exception)
        {
            return true;
        }

        bool IsUnder(string root) =>
            root.Length > 0 && full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

        if (full.Equals(windows, StringComparison.OrdinalIgnoreCase) || IsUnder(windows)) return true;
        if (programFiles.Length > 0 && (full.Equals(programFiles, StringComparison.OrdinalIgnoreCase) || IsUnder(programFiles))) return true;
        if (programFilesX86.Length > 0 && (full.Equals(programFilesX86, StringComparison.OrdinalIgnoreCase) || IsUnder(programFilesX86))) return true;
        if (programData.Length > 0 && (full.Equals(programData, StringComparison.OrdinalIgnoreCase) || IsUnder(programData))) return true;

        if (profile.Length > 0 && full.Equals(profile, StringComparison.OrdinalIgnoreCase)) return true;
        if (archives.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        if (full.Equals(archives, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var chars = new char[name.Length];
        int i = 0;

        foreach (char c in name.Trim())
            chars[i++] = invalid.Contains(c) || c == ' ' ? '_' : c;

        return new string(chars, 0, i);
    }

    public static void OpenArchives()
    {
        try
        {
            Directory.CreateDirectory(ArchivesDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + ArchivesDir + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.Log("SpaceService.OpenArchives FALLÓ", ex);
        }
    }

    public static string FormatSize(long bytes) => CleanerService.FormatSize(bytes);
}
