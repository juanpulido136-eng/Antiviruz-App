using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AntivirusApp.Engine;

public sealed class FolderWatcher : IDisposable
{
    private static readonly string[] IgnoredExtensions =
    {
        ".tmp", ".temp", ".part", ".partial", ".crdownload", ".!ut", ".download", ".opdownload"
    };

    private readonly FileSystemWatcher _watcher;
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event Action<string>? FileDetected;

    public string Folder { get; }

    public FolderWatcher(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnCreated;
        _watcher.Renamed += OnRenamed;
    }

    private void OnCreated(object sender, FileSystemEventArgs e) => Notify(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e) => Notify(e.FullPath);

    private void Notify(string path)
    {
        try
        {
            if (Directory.Exists(path) || !File.Exists(path))
            {
                return;
            }

            string extension = Path.GetExtension(path);

            if (IgnoredExtensions.Any(x => x.Equals(extension, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            DateTime now = DateTime.UtcNow;

            lock (_sync)
            {
                if (_recent.TryGetValue(path, out DateTime last) && (now - last).TotalSeconds < 30)
                {
                    return;
                }

                _recent[path] = now;

                if (_recent.Count > 500)
                {
                    foreach (string stale in _recent
                                 .Where(x => (now - x.Value).TotalMinutes > 5)
                                 .Select(x => x.Key)
                                 .ToList())
                    {
                        _recent.Remove(stale);
                    }
                }
            }

            FileDetected?.Invoke(path);
        }
        catch (Exception)
        {

        }
    }

    public static async Task<bool> WaitUntilReadyAsync(string path, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                await Task.Delay(350).ConfigureAwait(false);
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnCreated;
            _watcher.Renamed -= OnRenamed;
            _watcher.Dispose();
        }
        catch (Exception)
        {

        }
    }
}
