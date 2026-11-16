using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AntivirusApp.Services;

public static class AuthService
{
    private const string DefaultUser = "admin";

    public static string CurrentUser { get; set; } = DefaultUser;

    private static readonly string Folder =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aegis");

    private static string File_ => Path.Combine(Folder, "auth.json");

    public static string SignInLocal()
    {
        string user = DefaultUser;

        try
        {
            (string storedUser, _) = Load();

            if (!string.IsNullOrWhiteSpace(storedUser))
            {
                user = storedUser;
            }
        }
        catch (Exception)
        {
            user = DefaultUser;
        }

        CurrentUser = user;
        return user;
    }

    public static bool Validate(string user, string password)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        (string expectedUser, string expectedHash) = Load();

        if (!string.Equals(user, expectedUser, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string hash = Hash(password);

        return FixedTimeEquals(hash, expectedHash);
    }

    private static (string User, string Hash) Load()
    {
        try
        {
            if (!File.Exists(File_))
            {
                return (DefaultUser, Hash("aegis"));
            }

            var doc = JsonSerializer.Deserialize<AuthFile>(File.ReadAllText(File_, Encoding.UTF8));

            if (doc is null || string.IsNullOrWhiteSpace(doc.User) || string.IsNullOrWhiteSpace(doc.Sha256))
            {
                return (DefaultUser, Hash("aegis"));
            }

            return (doc.User, doc.Sha256.Trim().ToLowerInvariant());
        }
        catch (Exception)
        {
            return (DefaultUser, Hash("aegis"));
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left);
        byte[] b = Encoding.UTF8.GetBytes(right);

        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed class AuthFile
    {
        public string User { get; set; } = "";

        public string Sha256 { get; set; } = "";
    }
}
