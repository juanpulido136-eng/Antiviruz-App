using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AntivirusApp.Models;

namespace AntivirusApp.Services;

public static class CleanerService
{

    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "winsxs", "system32", "syswow64", "system volume information",
        "recovery", "windows.old", "driverstore", "softwaredistribution", "servicing",
        "assembly", "$recycle.bin", "$windows.~bt", "$windows.~ws", "config.msi",
        "installer", "prefetch", "boot", "perflogs", "wpsystem",
        "proc", "sys", "dev", "run", "snap", "node_modules", ".git", "__pycache__",
        "venv", ".venv", ".gradle"
    };

    private static readonly HashSet<string> AppNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "temp", "tmp", "packages", "cache", "caches", "crashdumps", "microsoft",
        "windows", "windowsapps", "common files", "modifiable", "virtualstore",
        "d3dscache", "placeholdertilelogfolder", "connecteddevicesplatform"
    };

    private static readonly Dictionary<string, string> Kinds = BuildKinds();

    private static Dictionary<string, string> BuildKinds()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Set(string kind, params string[] extensions)
        {
            foreach (var extension in extensions) map[extension] = kind;
        }

        Set("video", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg");
        Set("imagen", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".psd", ".raw", ".heic", ".svg");
        Set("audio", ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma");
        Set("documento", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".odt", ".epub", ".csv");
        Set("comprimido", ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab");
        Set("instalador", ".exe", ".msi", ".msix", ".appx", ".dmg", ".pkg", ".apk");
        Set("codigo", ".js", ".jsx", ".tsx", ".py", ".cs", ".java", ".php", ".html", ".css", ".json", ".sql", ".go", ".rs", ".ts");
        Set("base de datos", ".db", ".sqlite", ".sqlite3", ".mdf", ".accdb", ".dat");
        Set("cache", ".tmp", ".temp", ".log", ".bak", ".old", ".dmp", ".cache");

        return map;
    }

    public static CleanerScanResult Scan(CleanerOptions options, CleanerCounters counters, CancellationToken ct)
    {
        var result = new CleanerScanResult();

        var dirSize = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var dirFiles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dirTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        var heap = new PriorityQueue<CleanerItem, long>();
        var stack = new Stack<(string Path, int Depth)>();

        foreach (var root in options.Roots)
        {
            if (!string.IsNullOrWhiteSpace(root)) stack.Push((root, 0));
        }

        counters.Running = true;
        counters.Done = false;
        counters.Error = null;
        counters.Current = "Preparando";

        long files = 0, dirs = 0, bytes = 0, skipped = 0, errors = 0;

        try
        {
            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) break;

                var (path, depth) = stack.Pop();

                if (options.SkipSystem && IsSystemName(Path.GetFileName(path)))
                {
                    skipped++;
                    continue;
                }

                FileSystemInfo[] entries;
                try
                {
                    entries = new DirectoryInfo(path).GetFileSystemInfos();
                }
                catch (UnauthorizedAccessException) { errors++; continue; }
                catch (IOException) { errors++; continue; }
                catch (System.Security.SecurityException) { errors++; continue; }

                dirs++;

                if ((dirs & 0x3F) == 0)
                {
                    counters.Dirs = dirs;
                    counters.Files = files;
                    counters.Bytes = bytes;
                    counters.Current = path;
                }

                foreach (var entry in entries)
                {
                    if (ct.IsCancellationRequested) break;

                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                    if (!options.IncludeHidden && IsHidden(entry)) continue;

                    if (entry is DirectoryInfo)
                    {
                        if (options.SkipSystem && IsSystemName(entry.Name))
                        {
                            skipped++;
                            continue;
                        }

                        if (depth < 64) stack.Push((entry.FullName, depth + 1));
                        continue;
                    }

                    long size;
                    DateTime when;
                    try
                    {
                        var fi = (FileInfo)entry;
                        size = fi.Length;
                        when = fi.LastWriteTime;
                    }
                    catch (IOException) { errors++; continue; }
                    catch (UnauthorizedAccessException) { errors++; continue; }

                    files++;
                    bytes += size;

                    var parent = path;
                    for (int level = 0; level < 32 && !string.IsNullOrEmpty(parent); level++)
                    {
                        dirSize[parent] = dirSize.TryGetValue(parent, out var s0) ? s0 + size : size;
                        dirFiles[parent] = dirFiles.TryGetValue(parent, out var c0) ? c0 + 1 : 1;
                        if (!dirTime.TryGetValue(parent, out var t0) || when > t0) dirTime[parent] = when;

                        var up = Path.GetDirectoryName(parent);
                        if (string.IsNullOrEmpty(up) || string.Equals(up, parent, StringComparison.OrdinalIgnoreCase)) break;
                        parent = up;
                    }

                    if (size < options.MinFileBytes) continue;

                    var item = new CleanerItem
                    {
                        Path = entry.FullName,
                        Name = entry.Name,
                        Directory = path,
                        Size = size,
                        Modified = when,
                        Kind = KindOf(entry.Extension)
                    };

                    if (heap.Count < options.MaxFiles)
                    {
                        heap.Enqueue(item, size);
                    }
                    else if (heap.TryPeek(out _, out var smallest) && size > smallest)
                    {
                        heap.Dequeue();
                        heap.Enqueue(item, size);
                    }
                }
            }

            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

            while (heap.TryDequeue(out var kept, out _)) result.Files.Add(kept);
            result.Files.Sort((a, b) => b.Size.CompareTo(a.Size));

            var folders = new List<CleanerItem>();
            foreach (var pair in dirSize)
            {
                if (pair.Value < options.MinFolderBytes) continue;

                var name = Path.GetFileName(pair.Key);
                if (string.IsNullOrEmpty(name)) continue;

                folders.Add(new CleanerItem
                {
                    Path = pair.Key,
                    Name = name,
                    Directory = Path.GetDirectoryName(pair.Key) ?? string.Empty,
                    Size = pair.Value,
                    Files = dirFiles.TryGetValue(pair.Key, out var fc) ? fc : 0,
                    Modified = dirTime.TryGetValue(pair.Key, out var ft) ? ft : DateTime.MinValue,
                    IsFolder = true,
                    Kind = "carpeta"
                });
            }

            folders.Sort((a, b) => a.Size.CompareTo(b.Size));

            var keptFolders = new List<CleanerItem>();
            var sep = Path.DirectorySeparatorChar;

            foreach (var folder in folders)
            {
                var prefix = folder.Path.TrimEnd(sep) + sep;
                bool covered = false;

                foreach (var child in keptFolders)
                {
                    if (child.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        child.Size >= folder.Size * 0.95)
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered) keptFolders.Add(folder);
            }

            keptFolders.Reverse();
            result.Folders.AddRange(keptFolders);
        }
        catch (OperationCanceledException)
        {

            throw;
        }
        catch (Exception ex)
        {
            counters.Error = ex.GetType().Name + ": " + ex.Message;
            App.Log("Cleaner.Scan FALLÓ", ex);
        }
        finally
        {
            counters.Files = files;
            counters.Dirs = dirs;
            counters.Bytes = bytes;
            counters.Skipped = skipped;
            counters.Errors = errors;
            counters.Current = string.Empty;
            counters.Running = false;
            counters.Done = true;
        }

        return result;
    }

    public static List<(string Group, string Path)> AppRoots()
    {
        var list = new List<(string Group, string Path)>();

        void Add(string group, Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path)) list.Add((group, path));
        }

        Add("Programas", Environment.SpecialFolder.ProgramFiles);
        Add("Programas", Environment.SpecialFolder.ProgramFilesX86);
        Add("Datos de aplicación", Environment.SpecialFolder.LocalApplicationData);
        Add("Datos de aplicación", Environment.SpecialFolder.ApplicationData);
        Add("ProgramData", Environment.SpecialFolder.CommonApplicationData);

        foreach (var sub in new[] { "Downloads", "Documents", "Desktop", "Videos" })
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), sub);
            if (System.IO.Directory.Exists(path)) list.Add((sub, path));
        }

        return list;
    }

    public static List<AppUsage> MeasureApps(CleanerCounters counters, CancellationToken ct)
    {
        counters.Running = true;
        counters.Done = false;
        counters.Error = null;
        counters.Current = "Buscando aplicaciones";

        var targets = new List<(string Group, string Path)>();

        foreach (var (group, root) in AppRoots())
        {
            try
            {
                foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
                {
                    if (IsSystemName(dir.Name)) continue;
                    if (AppNoise.Contains(dir.Name)) continue;
                    if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    targets.Add((group, dir.FullName));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        var bag = new ConcurrentBag<AppUsage>();
        var done = 0;

        try
        {
            Parallel.ForEach(
                targets,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2)
                },
                target =>
                {
                    var usage = MeasureOne(target.Group, target.Path, ct);
                    if (usage is not null) bag.Add(usage);

                    var n = Interlocked.Increment(ref done);
                    counters.Dirs = n;
                    counters.Current = target.Path;
                });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            counters.Error = ex.GetType().Name + ": " + ex.Message;
            App.Log("Cleaner.MeasureApps FALLÓ", ex);
        }
        finally
        {
            counters.Running = false;
            counters.Done = true;
            counters.Current = string.Empty;
        }

        var apps = bag.ToList();
        apps.Sort((a, b) => b.Size.CompareTo(a.Size));
        return apps;
    }

    private static AppUsage? MeasureOne(string group, string path, CancellationToken ct)
    {
        long size = 0;
        int count = 0;
        var newest = DateTime.MinValue;
        var oldest = DateTime.MaxValue;

        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) return null;

            var current = stack.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(current).GetFileSystemInfos();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                if (entry is DirectoryInfo) { stack.Push(entry.FullName); continue; }

                try
                {
                    var fi = (FileInfo)entry;
                    size += fi.Length;
                    count++;
                    if (fi.LastWriteTime > newest) newest = fi.LastWriteTime;
                    if (fi.LastWriteTime < oldest) oldest = fi.LastWriteTime;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        if (size == 0) return null;

        return new AppUsage
        {
            Name = Path.GetFileName(path) ?? path,
            Path = path,
            Group = group,
            Size = size,
            Files = count,
            Newest = newest,
            Oldest = oldest == DateTime.MaxValue ? DateTime.MinValue : oldest
        };
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;

        public ushort fFlags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;

        public IntPtr hNameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);

    public static TrashResult MoveToTrash(IReadOnlyList<string> paths)
    {
        var result = new TrashResult();

        var valid = new List<string>();
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string path;
            try
            {
                path = Path.GetFullPath(raw);
            }
            catch (Exception) { continue; }

            if (!System.IO.Directory.Exists(path) && !File.Exists(path))
            {
                result.Failed.Add(path);
                continue;
            }

            valid.Add(path);
            sizes[path] = SizeOf(path);
        }

        if (valid.Count == 0) return result;

        int code = -1;
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", valid) + "\0\0",
                pTo = null,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI)
            };

            code = SHFileOperation(ref op);
        }
        catch (Exception ex)
        {
            App.Log("Cleaner.MoveToTrash FALLÓ", ex);
        }

        foreach (var path in valid)
        {
            bool gone = !System.IO.Directory.Exists(path) && !File.Exists(path);

            if (gone)
            {
                result.Moved.Add(path);
                result.Freed += sizes.TryGetValue(path, out var size) ? size : 0;
            }
            else
            {
                result.Failed.Add(path);
            }
        }

        App.Log($"Cleaner.MoveToTrash code={code} movidos={result.Moved.Count} fallos={result.Failed.Count} liberado={result.Freed}");
        return result;
    }

    public static void Reveal(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var full = Path.GetFullPath(path);
            var exists = File.Exists(full) || System.IO.Directory.Exists(full);

            if (exists && File.Exists(full))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = true });
                return;
            }

            var folder = exists ? full : Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) return;

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log("Cleaner.Reveal FALLÓ", ex);
        }
    }

    public static List<string> DefaultRoots()
    {
        var roots = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;
                    roots.Add(drive.RootDirectory.FullName);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex)
        {
            App.Log("Cleaner.DefaultRoots FALLÓ", ex);
        }

        if (roots.Count == 0) roots.Add("C:\\");
        return roots;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        double value = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };

        foreach (var unit in units)
        {
            value /= 1024.0;
            if (value < 1024 || unit == "TB") return value.ToString(value < 10 ? "0.00" : "0.0") + " " + unit;
        }

        return bytes + " B";
    }

    public static string KindOf(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return "otro";
        return Kinds.TryGetValue(extension, out var kind) ? kind : "otro";
    }

    private static bool IsHidden(FileSystemInfo entry) =>
        entry.Name.StartsWith(".", StringComparison.Ordinal) ||
        (entry.Attributes & FileAttributes.Hidden) != 0 ||
        (entry.Attributes & FileAttributes.System) != 0;

    private static bool IsSystemName(string? name) =>
        !string.IsNullOrEmpty(name) && SystemNames.Contains(name);

    private static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(current).GetFileSystemInfos();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                if (entry is DirectoryInfo) { stack.Push(entry.FullName); continue; }

                try
                {
                    total += ((FileInfo)entry).Length;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return total;
    }
}
