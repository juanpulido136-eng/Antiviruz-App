namespace AntivirusApp.Models;

public sealed class ScanOptions
{
    public ScanOptions(ScanMode mode)
        : this(mode, false)
    {
    }

    public ScanOptions(ScanMode mode, bool IncludeHidden)
    {
        Mode = mode;
        this.IncludeHidden = IncludeHidden;
    }

    public ScanMode Mode { get; }

    public bool IncludeHidden { get; }

    public long MaxBytes => Mode switch
    {
        ScanMode.Quick => 512L * 1024,
        ScanMode.Folder => 2L * 1024 * 1024,
        ScanMode.Deep => 16L * 1024 * 1024,
        _ => 4L * 1024 * 1024
    };

    public int MaliciousThreshold => 70;

    public int SuspiciousThreshold => 35;
}
