using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AntivirusApp.Engine;

public static class DocumentInspector
{
    private static readonly string[] PdfDangerous =
    {
        "/JavaScript", "/JS", "/OpenAction", "/AA", "/Launch", "/EmbeddedFile",
        "/RichMedia", "/XFA", "/SubmitForm", "/GoToE"
    };

    private static readonly string[] MacroDangerous =
    {
        "autoopen", "document_open", "workbook_open", "auto_close", "autoexec",
        "createobject", "wscript.shell", "shell(", "environ(", "urldownloadtofile",
        "adodb.stream", "powershell", "cmd.exe", "chr(", "base64"
    };

    private static readonly string[] LnkDangerous =
    {
        "powershell", "-enc", "-w hidden", "cmd.exe", "mshta", "wscript", "cscript",
        "http://", "https://", "rundll32", "regsvr32", "certutil"
    };

    public static bool IsOoxml(string extension) => extension is ".docm" or ".xlsm" or ".pptm" or ".docx" or ".xlsx" or ".pptx";

    public static int Score(string path, string extension, byte[] prefix, List<string> reasons, int cap = 55)
    {
        try
        {
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return ScorePdf(prefix, reasons, cap);
            }

            if (IsOoxml(extension))
            {
                return ScoreOffice(path, extension, reasons, Math.Max(cap, 65));
            }

            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return ScoreLnk(prefix, reasons, cap);
            }
        }
        catch (Exception)
        {

        }

        return 0;
    }

    private static int ScorePdf(byte[] data, List<string> reasons, int cap)
    {
        string text = Encoding.Latin1.GetString(data);
        int score = 0;

        void Add(int weight, string reason)
        {
            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }

            score = Math.Min(cap, score + weight);
        }

        if (text.Contains("/JavaScript", StringComparison.Ordinal) || text.Contains("/JS", StringComparison.Ordinal))
        {
            Add(30, "PDF con JavaScript embebido");
        }

        if (text.Contains("/OpenAction", StringComparison.Ordinal) || text.Contains("/AA", StringComparison.Ordinal))
        {
            Add(20, "PDF que ejecuta una acción al abrirse");
        }

        if (text.Contains("/Launch", StringComparison.Ordinal))
        {
            Add(25, "PDF que lanza un programa externo (/Launch)");
        }

        if (text.Contains("/EmbeddedFile", StringComparison.Ordinal))
        {
            Add(15, "PDF con archivos incrustados");
        }

        if (text.Contains("/RichMedia", StringComparison.Ordinal) || text.Contains("/XFA", StringComparison.Ordinal))
        {
            Add(10, "PDF con contenido activo (RichMedia/XFA)");
        }

        return score;
    }

    private static int ScoreOffice(string path, string extension, List<string> reasons, int cap)
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

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        string? macroEntry = zip.Entries
            .FirstOrDefault(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
            ?.FullName;

        if (macroEntry is null)
        {
            return 0;
        }

        Add(35, $"Documento Office con macros VBA ({Path.GetFileName(macroEntry)})");

        string vba;

        using (var entry = zip.GetEntry(macroEntry)!.Open())
        using (var reader = new StreamReader(entry, Encoding.Latin1))
        {
            vba = reader.ReadToEnd();
        }

        string lower = vba.ToLowerInvariant();
        int hits = 0;

        foreach (string pattern in MacroDangerous)
        {
            if (lower.Contains(pattern, StringComparison.Ordinal))
            {
                hits++;
            }
        }

        if (lower.Contains("autoopen", StringComparison.Ordinal) ||
            lower.Contains("document_open", StringComparison.Ordinal) ||
            lower.Contains("workbook_open", StringComparison.Ordinal))
        {
            Add(20, "Macro que se ejecuta automáticamente al abrir el documento");
        }

        if (lower.Contains("createobject", StringComparison.Ordinal) ||
            lower.Contains("wscript.shell", StringComparison.Ordinal) ||
            lower.Contains("shell(", StringComparison.Ordinal))
        {
            Add(20, "Macro que puede ejecutar comandos del sistema");
        }

        if (hits >= 4)
        {
            Add(10, $"Macro con {hits} indicadores sospechosos");
        }

        return score;
    }

    private static int ScoreLnk(byte[] data, List<string> reasons, int cap)
    {

        if (data.Length < 0x4C || data[0] != 0x4C || data[1] != 0 || data[2] != 0 || data[3] != 0)
        {
            return 0;
        }

        string ascii = Encoding.Latin1.GetString(data).ToLowerInvariant();
        string wide = Encoding.Unicode.GetString(data).ToLowerInvariant();
        string both = ascii + "\n" + wide;

        int score = 0;

        void Add(int weight, string reason)
        {
            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }

            score = Math.Min(cap, score + weight);
        }

        foreach (string pattern in LnkDangerous)
        {
            if (both.Contains(pattern, StringComparison.Ordinal))
            {
                Add(18, $"Acceso directo con '{pattern.Trim()}' en el destino o los argumentos");
            }
        }

        if (score > 0)
        {
            Add(10, "Acceso directo que ejecuta un intérprete de comandos");
        }

        return score;
    }
}
