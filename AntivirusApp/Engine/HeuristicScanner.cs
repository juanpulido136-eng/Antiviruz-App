using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AntivirusApp.Models;

namespace AntivirusApp.Engine
{

    public static class HeuristicScanner
    {

        public const string Version = "v22.0";

        private const long MaxHashBytes = 32L * 1024 * 1024;

        private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".scr", ".com", ".pif", ".cpl", ".sys", ".ocx", ".drv"
        };

        private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".hta", ".jar", ".msi", ".reg", ".lnk", ".scf", ".chm"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm",
            ".ppt", ".pptx", ".pptm", ".rtf", ".txt", ".jpg", ".jpeg",
            ".png", ".gif", ".mp3", ".mp4", ".avi", ".mkv"
        };

        private static readonly Dictionary<string, string> KnownBadHashes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f"] =
                    "Archivo de prueba EICAR"
            };

        private static readonly (string Pattern, int Weight, string Reason)[] Patterns =
        {
            ("eicar-standard-antivirus-test-file", 100, "Firma de prueba EICAR"),
            ("vssadmin delete shadows", 30, "Intento de borrar copias de sombra de Windows"),
            ("wbadmin delete catalog", 30, "Borrado del catálogo de copias de seguridad"),
            ("autoopen", 30, "Macro AutoOpen de Office"),
            ("document_open", 30, "Macro Document_Open de Office"),
            ("workbook_open", 30, "Macro Workbook_Open de Office"),
            ("bcdedit /set", 25, "Modificación de la configuración de arranque"),
            ("certutil -decode", 20, "Uso de certutil para decodificar carga útil"),
            ("powershell -windowstyle hidden", 20, "PowerShell oculto"),
            ("upx!", 20, "Binario empaquetado con UPX"),
            ("mshta ", 15, "Ejecución a través de mshta"),
            ("certutil -urlcache", 15, "Descarga de archivos vía certutil"),
            ("downloadstring", 15, "Descarga y ejecución de código remoto"),
            ("createobject(\"wscript", 15, "Automatización WScript"),
            ("-encodedcommand", 15, "PowerShell con comando codificado"),
            ("bitsadmin /transfer", 15, "Transferencia de archivos vía BITS"),
            ("auto_close", 15, "Macro Auto_Close de Office"),
            ("frombase64string", 12, "Decodificación Base64 embebida"),
            ("invoke-expression", 12, "Ejecución dinámica de código (IEX)"),
            ("schtasks /create", 12, "Creación de tarea programada"),
            ("wscript.shell", 12, "Acceso a WScript.Shell"),
            ("net user ", 10, "Manipulación de cuentas locales"),
            ("downloadfile", 10, "Descarga remota de archivos"),
            ("reg add ", 8, "Modificación del registro"),
            ("chr(34)", 5, "Construcción ofuscada de cadenas"),
            (".locked", 5, "Posible extensión de cifrado")
        };

        private const int MaxPatternScore = 45;

        public static ScanResult SafeScan(string path, ScanOptions options, IProgress<int>? progress = null)
        {
            options ??= new ScanOptions(ScanMode.Automatic);

            var started = Stopwatch.StartNew();

            try
            {
                if (Directory.Exists(path))
                {
                    return ScanDirectory(path, options, progress, started);
                }

                return ScanOneFile(path, options, progress, started);
            }
            catch (Exception ex)
            {
                return new ScanResult
                {
                    Verdict = Verdicts.Clean,
                    RiskScore = 0,
                    Reasons = new[] { "No se pudo analizar: " + ex.Message },
                    IsError = true,
                    Elapsed = started.Elapsed
                };
            }
        }

        private static ScanResult ScanDirectory(
            string folder,
            ScanOptions options,
            IProgress<int>? progress,
            Stopwatch clock)
        {
            var reasons = new List<string>();
            int worst = 0;
            string verdict = Verdicts.Clean;
            long totalSize = 0;
            int scanned = 0;

            var mode = options.IncludeHidden
                ? new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                    ReturnSpecialDirectories = false
                }
                : new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                    ReturnSpecialDirectories = false
                };

            List<string> files;

            try
            {
                files = Directory.EnumerateFiles(folder, "*", mode).ToList();
            }
            catch (Exception ex)
            {
                return new ScanResult
                {
                    Verdict = Verdicts.Clean,
                    RiskScore = 0,
                    Reasons = new[] { "No se pudo recorrer la carpeta: " + ex.Message },
                    IsError = true,
                    Elapsed = clock.Elapsed
                };
            }

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];

                var one = ScanOneFile(file, options, null, clock);

                scanned++;
                totalSize += one.Size;

                if (Rank(one.Verdict) > Rank(verdict))
                {
                    verdict = one.Verdict;
                }

                worst = Math.Max(worst, one.RiskScore);

                foreach (string reason in one.Reasons)
                {
                    if (reason != "Sin indicios heurísticos" && reasons.Count < 40)
                    {
                        reasons.Add($"{Path.GetFileName(file)}: {reason}");
                    }
                }

                progress?.Report((int)((i + 1) * 100.0 / Math.Max(1, files.Count)));
            }

            if (reasons.Count == 0)
            {
                reasons.Add($"Sin indicios heurísticos en {scanned} archivos");
            }

            return new ScanResult
            {
                Verdict = verdict,
                RiskScore = worst,
                Reasons = reasons,
                Size = totalSize,
                Elapsed = clock.Elapsed
            };
        }

        private static ScanResult ScanOneFile(
            string path,
            ScanOptions options,
            IProgress<int>? progress,
            Stopwatch clock)
        {
            var reasons = new List<string>();
            int score = 0;
            long size = 0;
            string sha = "";

            try
            {
                var info = new FileInfo(path);

                if (!info.Exists)
                {
                    return new ScanResult
                    {
                        Verdict = Verdicts.Clean,
                        Reasons = new[] { "El archivo ya no existe" },
                        IsError = true
                    };
                }

                size = info.Length;
                progress?.Report(10);

                sha = ComputeSha256(path);

                if (KnownBadHashes.TryGetValue(sha, out string? known))
                {
                    score = 100;
                    reasons.Add(known);
                }

                progress?.Report(30);

                if (size == 0)
                {
                    reasons.Add("Archivo vacío");
                }

                long readLimit = Math.Min(size, options.MaxBytes);
                byte[] data = ReadPrefix(path, readLimit);

                if (readLimit > 0 && size - readLimit > 1024 * 1024)
                {
                    reasons.Add(
                        $"Análisis parcial: {readLimit / (1024.0 * 1024):0.#} MB revisados " +
                        $"de {size / (1024.0 * 1024):0.#} MB");
                }

                string extension = Path.GetExtension(path);
                string header = Sniff(data);

                if (header == "MZ" && DocumentExtensions.Contains(extension))
                {
                    reasons.Add($"Ejecutable disfrazado de '{extension}'");
                    score += 35;
                }
                else if (header == "MZ" && !ExecutableExtensions.Contains(extension))
                {
                    reasons.Add("Cabecera de ejecutable con extensión no ejecutable");
                    score += 30;
                }
                else if (ExecutableExtensions.Contains(extension) && header != "MZ" && data.Length >= 16)
                {

                    reasons.Add(
                        $"Extensión ejecutable sin cabecera de programa ({header}): " +
                        "archivo renombrado o dañado");
                    score += 30;
                }

                string name = Path.GetFileName(path);
                string[] parts = name.Split('.');

                if (parts.Length >= 3)
                {
                    string last = "." + parts[^1];
                    string previous = "." + parts[^2];

                    if ((ExecutableExtensions.Contains(last) || ScriptExtensions.Contains(last))
                        && DocumentExtensions.Contains(previous))
                    {
                        reasons.Add($"Doble extensión ({previous}{last})");
                        score += 25;
                    }
                }

                if (ExecutableExtensions.Contains(extension))
                {
                    reasons.Add($"Ejecutable de Windows ({extension})");
                    score += 8;
                }
                else if (ScriptExtensions.Contains(extension))
                {
                    reasons.Add($"Script o instalador ({extension})");
                    score += 12;
                }

                if (info.Attributes.HasFlag(FileAttributes.Hidden)
                    && info.Attributes.HasFlag(FileAttributes.System))
                {
                    reasons.Add("Archivo oculto y de sistema");
                    score += 10;
                }

                if (header == "MZ")
                {

                    byte[] peData = size <= 32L * 1024 * 1024 ? ReadPrefix(path, size) : data;
                    var pe = PeInspector.Inspect(peData);

                    if (pe.IsValid)
                    {
                        score += PeInspector.Score(pe, reasons);
                    }
                }
                else if (DocumentInspector.IsOoxml(extension)
                         || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                         || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    score += DocumentInspector.Score(path, extension, data, reasons);
                }
                else if (header == "ZIP")
                {
                    score += ArchiveInspector.Score(path, reasons);
                }

                progress?.Report(55);

                if (data.Length >= 4096)
                {
                    double entropy = Shannon(data);

                    if (entropy >= 7.6)
                    {
                        reasons.Add($"Entropía muy alta ({entropy:0.0}): cifrado o empaquetado");
                        score += 25;
                    }
                    else if (entropy >= 7.2)
                    {
                        reasons.Add($"Entropía alta ({entropy:0.0}): posible binario empaquetado");
                        score += 15;
                    }
                }

                progress?.Report(75);

                if (data.Length > 0)
                {
                    string text = Encoding.Latin1.GetString(data).ToLowerInvariant();
                    int patternScore = 0;

                    foreach (var (pattern, weight, reason) in Patterns)
                    {
                        if (!text.Contains(pattern, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!reasons.Contains(reason))
                        {
                            reasons.Add(reason);
                        }

                        patternScore = Math.Min(MaxPatternScore, patternScore + weight);
                    }

                    score += patternScore;
                }

                if (score > 100)
                {
                    score = 100;
                }

                progress?.Report(100);
            }
            catch (UnauthorizedAccessException)
            {
                return new ScanResult
                {
                    Verdict = Verdicts.Clean,
                    Reasons = new[] { "Sin permisos para leer el archivo" },
                    IsError = true
                };
            }
            catch (IOException ex)
            {
                return new ScanResult
                {
                    Verdict = Verdicts.Clean,
                    Reasons = new[] { "Archivo bloqueado o en uso: " + ex.Message },
                    IsError = true
                };
            }

            string verdict = score >= options.MaliciousThreshold
                ? Verdicts.Malicious
                : score >= options.SuspiciousThreshold
                    ? Verdicts.Suspicious
                    : Verdicts.Clean;

            if (reasons.Count == 0)
            {
                reasons.Add("Sin indicios heurísticos");
            }

            return new ScanResult
            {
                Verdict = verdict,
                RiskScore = score,
                Reasons = reasons,
                Sha256 = sha,
                Size = size,
                Elapsed = clock.Elapsed
            };
        }

        private static int Rank(string verdict) => verdict switch
        {
            Verdicts.Malicious => 3,
            Verdicts.Suspicious => 2,
            Verdicts.Error => 1,
            _ => 0
        };

        private static string Sniff(byte[] data)
        {
            if (data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A) return "MZ";
            if (data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04) return "ZIP";
            if (data.Length >= 4 && data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46) return "PDF";
            if (data.Length >= 4 && data[0] == 0x7F && data[1] == 0x45 && data[2] == 0x4C && data[3] == 0x46) return "ELF";
            if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x61 && data[2] == 0x72 && data[3] == 0x21) return "RAR";
            if (data.Length >= 4 && data[0] == 0x4D && data[1] == 0x53 && data[2] == 0x43 && data[3] == 0x46) return "CAB";
            return "DESCONOCIDO";
        }

        private static byte[] ReadPrefix(string path, long maxBytes)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            long length = Math.Min(stream.Length, maxBytes);

            if (length <= 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[length];
            int read = 0;

            while (read < buffer.Length)
            {
                int chunk = stream.Read(buffer, read, buffer.Length - read);

                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            return read == buffer.Length ? buffer : buffer[..read];
        }

        private static string ComputeSha256(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var hash = SHA256.Create();

                long limit = Math.Min(stream.Length, MaxHashBytes);
                var buffer = new byte[64 * 1024];
                long total = 0;

                while (total < limit)
                {
                    int wanted = (int)Math.Min(buffer.Length, limit - total);
                    int chunk = stream.Read(buffer, 0, wanted);

                    if (chunk <= 0)
                    {
                        break;
                    }

                    hash.TransformBlock(buffer, 0, chunk, null, 0);
                    total += chunk;
                }

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                return hash.Hash is null
                    ? ""
                    : Convert.ToHexString(hash.Hash).ToLowerInvariant();
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static double Shannon(byte[] data)
        {
            if (data.Length == 0)
            {
                return 0;
            }

            Span<int> counts = stackalloc int[256];

            foreach (byte b in data)
            {
                counts[b]++;
            }

            double entropy = 0;
            double length = data.Length;

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == 0)
                {
                    continue;
                }

                double p = counts[i] / length;
                entropy -= p * Math.Log2(p);
            }

            return entropy;
        }
    }
}
