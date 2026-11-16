using System;
using System.Collections.Generic;

namespace AntivirusApp.Models;

public sealed class ScanResult
{

    public string Verdict { get; set; } = Verdicts.Clean;

    public int RiskScore { get; set; }

    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();

    public string Sha256 { get; set; } = "";

    public long Size { get; set; }

    public TimeSpan Elapsed { get; set; }

    public bool IsError { get; set; }
}
