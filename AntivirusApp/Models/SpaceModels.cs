using System;
using System.Collections.Generic;

namespace AntivirusApp.Models;

public sealed class FolderUsage
{
    public string Path = string.Empty;
    public string Name = string.Empty;
    public long Size;
    public int Files;
    public DateTime Modified;
    public bool Protected;
}

public sealed class KindUsage
{
    public string Kind = "otro";
    public long Size;
    public int Files;
}

public sealed class SpaceReport
{
    public string Root = string.Empty;
    public long Total;
    public long Files;
    public List<FolderUsage> Folders { get; } = new();
    public List<KindUsage> Kinds { get; } = new();
    public TimeSpan Elapsed;
}

public sealed class SpaceCounters
{
    public string Current = string.Empty;
    public long Folders;
    public long Files;
    public long Bytes;
    public bool Done;
}

public sealed class ZipOutcome
{
    public string Folder = string.Empty;
    public string ZipPath = string.Empty;
    public long OriginalBytes;
    public long ZipBytes;
    public int Files;
    public bool Verified;
    public bool Deleted;
    public string? Error;

    public bool Ok => Verified && Error is null;

    public double Ratio => OriginalBytes <= 0 ? 0 : ZipBytes * 100.0 / OriginalBytes;
}
