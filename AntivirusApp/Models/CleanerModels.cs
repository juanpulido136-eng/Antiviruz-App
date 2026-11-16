using System;
using System.Collections.Generic;

namespace AntivirusApp.Models;

public sealed class CleanerOptions
{
    public CleanerOptions(IReadOnlyList<string> roots) => Roots = roots;

    public IReadOnlyList<string> Roots { get; }

    public long MinFileBytes { get; set; } = 50L * 1024 * 1024;

    public long MinFolderBytes { get; set; } = 120L * 1024 * 1024;

    public bool SkipSystem { get; set; } = true;

    public bool IncludeHidden { get; set; }

    public int MaxFiles { get; set; } = 600;
}

public sealed class CleanerCounters
{
    public long Files;
    public long Dirs;
    public long Bytes;
    public long Skipped;
    public long Errors;
    public string Current = string.Empty;
    public bool Running;
    public bool Done;
    public string? Error;
}

public sealed class CleanerItem
{
    public string Path = string.Empty;
    public string Name = string.Empty;
    public string Directory = string.Empty;
    public long Size;
    public int Files;
    public DateTime Modified;
    public bool IsFolder;
    public string Kind = "otro";
}

public sealed class CleanerScanResult
{
    public List<CleanerItem> Files { get; } = new();

    public List<CleanerItem> Folders { get; } = new();
}

public sealed class AppUsage
{
    public string Name = string.Empty;
    public string Path = string.Empty;
    public string Group = string.Empty;
    public long Size;
    public int Files;
    public DateTime Newest;
    public DateTime Oldest;
}

public sealed class TrashResult
{
    public List<string> Moved { get; } = new();

    public List<string> Failed { get; } = new();

    public long Freed;

    public bool Ok => Moved.Count > 0;
}
