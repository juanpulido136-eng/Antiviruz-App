using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AntivirusApp.Engine;

public sealed class PeSection
{
    public string Name { get; init; } = "";

    public uint VirtualAddress { get; init; }

    public uint VirtualSize { get; init; }

    public uint RawSize { get; init; }

    public uint RawOffset { get; init; }

    public double Entropy { get; init; }

    public uint Characteristics { get; init; }

    public bool IsExecutable => (Characteristics & 0x20000000) != 0;

    public bool IsWritable => (Characteristics & 0x80000000) != 0;
}

public sealed class PeInfo
{
    public bool IsValid { get; set; }

    public bool Is64Bit { get; set; }

    public ushort Machine { get; set; }

    public bool IsDll { get; set; }

    public string? EntryPointSection { get; set; }

    public List<PeSection> Sections { get; } = new();

    public List<string> Imports { get; } = new();

    public long OverlaySize { get; set; }

    public long FileSize { get; set; }

    public string? Packer { get; set; }

    public bool HasWritableExecutableSection { get; set; }

    public double MaxSectionEntropy { get; set; }

    public bool EntryPointInLastSection { get; set; }
}

public static class PeInspector
{
    private static readonly (string Dll, string Api, int Weight, string Reason)[] SuspiciousImports =
    {
        ("", "VirtualAllocEx", 25, "Reserva memoria en otro proceso (inyección)"),
        ("", "WriteProcessMemory", 30, "Escribe en la memoria de otro proceso (inyección)"),
        ("", "CreateRemoteThread", 30, "Crea un hilo en otro proceso (inyección)"),
        ("", "NtWriteVirtualMemory", 30, "Escribe memoria remota vía NTAPI"),
        ("", "RtlCreateUserThread", 25, "Creación de hilo remoto vía NTAPI"),
        ("", "SetThreadContext", 20, "Modifica el contexto de un hilo (hollowing)"),
        ("", "QueueUserAPC", 20, "Inyección por APC"),
        ("", "SetWindowsHookEx", 20, "Instala un hook global (posible keylogger)"),
        ("", "GetAsyncKeyState", 20, "Captura de pulsaciones de teclado"),
        ("", "IsDebuggerPresent", 10, "Comprobación anti-depuración"),
        ("", "CheckRemoteDebuggerPresent", 12, "Comprobación anti-depuración"),
        ("", "NtQueryInformationProcess", 10, "Consulta de información del proceso (anti-debug)"),
        ("", "VirtualProtect", 15, "Cambia protecciones de memoria (RWX)"),
        ("", "URLDownloadToFile", 25, "Descarga un archivo desde Internet"),
        ("", "URLDownloadToCacheFile", 20, "Descarga un archivo desde Internet"),
        ("", "InternetOpenUrl", 15, "Conexión HTTP directa"),
        ("", "WinHttpOpen", 10, "Conexión HTTP directa"),
        ("", "CryptEncrypt", 20, "Cifrado de datos (posible ransomware)"),
        ("", "CryptGenKey", 15, "Generación de claves"),
        ("", "RegSetValue", 10, "Escritura en el registro (persistencia)"),
        ("", "RegCreateKey", 8, "Creación de claves de registro"),
        ("", "ShellExecute", 8, "Ejecución de comandos o archivos"),
        ("", "WinExec", 12, "Ejecución de comandos"),
        ("", "CreateProcess", 6, "Creación de procesos"),
        ("", "LoadLibrary", 4, "Carga dinámica de bibliotecas"),
        ("", "GetProcAddress", 4, "Resolución dinámica de funciones")
    };

    private static readonly string[] PackerNames =
    {
        "upx0", "upx1", "upx2", ".aspack", ".adata", "themida", ".vmp0", ".vmp1",
        ".petite", ".nsp0", ".mpress", ".packed", "pec1", "pec2", ".mpcw"
    };

    public static PeInfo Inspect(byte[] data)
    {
        var info = new PeInfo { FileSize = data.Length };

        try
        {
            if (data.Length < 0x100 || data[0] != 0x4D || data[1] != 0x5A)
            {
                return info;
            }

            int peOffset = BitConverter.ToInt32(data, 0x3C);

            if (peOffset <= 0 || peOffset + 24 >= data.Length)
            {
                return info;
            }

            if (data[peOffset] != 0x50 || data[peOffset + 1] != 0x45 ||
                data[peOffset + 2] != 0 || data[peOffset + 3] != 0)
            {
                return info;
            }

            info.IsValid = true;

            int coff = peOffset + 4;
            info.Machine = BitConverter.ToUInt16(data, coff);
            int sectionCount = BitConverter.ToUInt16(data, coff + 2);
            int optionalSize = BitConverter.ToUInt16(data, coff + 16);
            ushort characteristics = BitConverter.ToUInt16(data, coff + 18);

            info.IsDll = (characteristics & 0x2000) != 0;

            int optional = coff + 20;

            if (optional + 2 > data.Length)
            {
                return info;
            }

            ushort magic = BitConverter.ToUInt16(data, optional);
            info.Is64Bit = magic == 0x20B;

            int dirOffset = optional + (info.Is64Bit ? 112 : 96);
            uint entryRva = BitConverter.ToUInt32(data, optional + 16);

            int sectionTable = optional + optionalSize;

            if (sectionTable + sectionCount * 40 > data.Length)
            {
                sectionCount = Math.Max(0, (data.Length - sectionTable) / 40);
            }

            for (int i = 0; i < sectionCount; i++)
            {
                int s = sectionTable + i * 40;
                string name = ReadName(data, s, 8);

                var section = new PeSection
                {
                    Name = name,
                    VirtualSize = BitConverter.ToUInt32(data, s + 8),
                    VirtualAddress = BitConverter.ToUInt32(data, s + 12),
                    RawSize = BitConverter.ToUInt32(data, s + 16),
                    RawOffset = BitConverter.ToUInt32(data, s + 20),
                    Characteristics = BitConverter.ToUInt32(data, s + 36),
                    Entropy = Entropy(data, (long)BitConverter.ToUInt32(data, s + 20),
                        BitConverter.ToUInt32(data, s + 16))
                };

                info.Sections.Add(section);

                if (section.Entropy > info.MaxSectionEntropy)
                {
                    info.MaxSectionEntropy = section.Entropy;
                }

                if (section.IsExecutable && section.IsWritable)
                {
                    info.HasWritableExecutableSection = true;
                }

                if (PackerNames.Any(p => name.TrimStart('.').StartsWith(p, StringComparison.OrdinalIgnoreCase)
                                         || name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    info.Packer ??= name;
                }

                if (entryRva >= section.VirtualAddress &&
                    entryRva < section.VirtualAddress + Math.Max(section.VirtualSize, section.RawSize))
                {
                    info.EntryPointSection = section.Name;
                    info.EntryPointInLastSection = i == sectionCount - 1 && sectionCount > 1;
                }
            }

            long end = 0;

            foreach (var section in info.Sections)
            {
                end = Math.Max(end, (long)section.RawOffset + section.RawSize);
            }

            info.OverlaySize = Math.Max(0, data.Length - end);

            ReadImports(data, info, dirOffset);
        }
        catch (Exception)
        {

        }

        return info;
    }

    private static void ReadImports(byte[] data, PeInfo info, int dirOffset)
    {
        try
        {
            if (dirOffset + 16 > data.Length)
            {
                return;
            }

            uint importRva = BitConverter.ToUInt32(data, dirOffset + 8);
            uint importSize = BitConverter.ToUInt32(data, dirOffset + 12);

            if (importRva == 0 || importSize == 0)
            {
                return;
            }

            long table = RvaToOffset(info, importRva);

            if (table <= 0 || table >= data.Length)
            {
                return;
            }

            for (int i = 0; i < 64; i++)
            {
                long descriptor = table + i * 20;

                if (descriptor + 20 > data.Length)
                {
                    break;
                }

                uint originalFirstThunk = BitConverter.ToUInt32(data, (int)descriptor);
                uint nameRva = BitConverter.ToUInt32(data, (int)descriptor + 12);
                uint firstThunk = BitConverter.ToUInt32(data, (int)descriptor + 16);

                if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
                {
                    break;
                }

                long thunkRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
                long thunk = RvaToOffset(info, (uint)thunkRva);

                if (thunk <= 0)
                {
                    continue;
                }

                int width = info.Is64Bit ? 8 : 4;

                for (int t = 0; t < 512; t++)
                {
                    long pos = thunk + t * width;

                    if (pos + width > data.Length)
                    {
                        break;
                    }

                    ulong value = info.Is64Bit
                        ? BitConverter.ToUInt64(data, (int)pos)
                        : BitConverter.ToUInt32(data, (int)pos);

                    if (value == 0)
                    {
                        break;
                    }

                    ulong highBit = info.Is64Bit ? 0x8000000000000000UL : 0x80000000UL;

                    if ((value & highBit) != 0)
                    {
                        continue;
                    }

                    long nameOffset = RvaToOffset(info, (uint)(value & 0x7FFFFFFF));

                    if (nameOffset <= 0 || nameOffset + 2 >= data.Length)
                    {
                        continue;
                    }

                    string? api = ReadCString(data, nameOffset + 2, 128);

                    if (!string.IsNullOrEmpty(api) && !info.Imports.Contains(api))
                    {
                        info.Imports.Add(api);
                    }
                }
            }
        }
        catch (Exception)
        {

        }
    }

    private static long RvaToOffset(PeInfo info, uint rva)
    {
        foreach (var section in info.Sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.RawSize))
            {
                return section.RawOffset + (rva - section.VirtualAddress);
            }
        }

        return rva;
    }

    private static string ReadName(byte[] data, int offset, int length)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < length && offset + i < data.Length; i++)
        {
            byte b = data[offset + i];

            if (b == 0)
            {
                break;
            }

            sb.Append((char)b);
        }

        return sb.ToString();
    }

    private static string? ReadCString(byte[] data, long offset, int max)
    {
        var sb = new StringBuilder();

        for (long i = offset; i < data.Length && i < offset + max; i++)
        {
            byte b = data[(int)i];

            if (b == 0)
            {
                break;
            }

            if (b < 0x20 || b > 0x7E)
            {
                return null;
            }

            sb.Append((char)b);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static double Entropy(byte[] data, long offset, long length)
    {
        if (offset < 0 || length <= 0 || offset >= data.Length)
        {
            return 0;
        }

        long end = Math.Min(data.Length, offset + length);
        long count = end - offset;

        if (count < 512)
        {
            return 0;
        }

        Span<int> counts = stackalloc int[256];

        for (long i = offset; i < end; i++)
        {
            counts[data[(int)i]]++;
        }

        double entropy = 0;

        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            double p = counts[i] / (double)count;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    public static int Score(PeInfo info, List<string> reasons, int cap = 65)
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

        foreach (var (_, api, weight, reason) in SuspiciousImports)
        {
            if (info.Imports.Contains(api, StringComparer.OrdinalIgnoreCase))
            {
                Add(weight, $"Importa {api}: {reason}");
            }
        }

        if (info.Packer is not null)
        {
            Add(30, $"Empaquetado con {info.Packer.Trim()} (binario comprimido/ofuscado)");
        }

        if (info.HasWritableExecutableSection)
        {
            Add(20, "Sección escribible y ejecutable (técnica típica de packers)");
        }

        if (info.EntryPointInLastSection)
        {
            Add(15, "Punto de entrada en la última sección (típico de packers)");
        }

        if (info.MaxSectionEntropy >= 7.9)
        {
            Add(20, $"Sección con entropía {info.MaxSectionEntropy:0.00} (cifrada o comprimida)");
        }

        if (info.OverlaySize > 1024 * 1024)
        {
            Add(10, $"Overlay de {info.OverlaySize / (1024.0 * 1024):0.#} MB tras las secciones");
        }

        return score;
    }
}
