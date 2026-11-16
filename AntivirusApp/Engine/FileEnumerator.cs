using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AntivirusApp.Models;

namespace AntivirusApp.Engine;

public static class FileEnumerator
{
    public static IEnumerable<string> Enumerate(
        string folder,
        ScanOptions options,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = options.IncludeHidden
                ? FileAttributes.None
                : FileAttributes.Hidden | FileAttributes.System
        };

        IEnumerator<string> enumerator;

        try
        {
            enumerator = Directory.EnumerateFiles(folder, "*", enumeration).GetEnumerator();
        }
        catch (Exception)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool moved;

                try
                {
                    moved = enumerator.MoveNext();
                }
                catch (Exception)
                {
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                string current = enumerator.Current;

                if (!string.IsNullOrEmpty(current))
                {
                    yield return current;
                }
            }
        }
    }
}
