using System;
using System.IO;
using System.Text.Json;

namespace AntivirusApp.Services;

public sealed class UserProfile
{
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string AvatarPath { get; set; } = "";
}

public static class ProfileStore
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aegis");

    public static string AvatarFile => Path.Combine(Folder, "avatar.png");

    private static string File_ => Path.Combine(Folder, "profile.json");

    public static event Action? Changed;

    public static UserProfile Load()
    {
        try
        {
            if (!File.Exists(File_)) return new UserProfile();

            var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(File_));
            if (profile is null) return new UserProfile();

            if (!string.IsNullOrWhiteSpace(profile.AvatarPath) && !File.Exists(profile.AvatarPath))
                profile.AvatarPath = "";

            return profile;
        }
        catch (Exception ex)
        {
            App.Log("ProfileStore.Load FALLÓ", ex);
            return new UserProfile();
        }
    }

    public static void Save(UserProfile profile)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            File.WriteAllText(File_, JsonSerializer.Serialize(profile, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            App.Log("ProfileStore.Save FALLÓ", ex);
        }
    }

    public static string SetAvatar(string sourcePath)
    {
        Directory.CreateDirectory(Folder);
        File.Copy(sourcePath, AvatarFile, true);
        return AvatarFile;
    }

    public static void ClearAvatar()
    {
        try
        {
            if (File.Exists(AvatarFile)) File.Delete(AvatarFile);
        }
        catch (Exception ex)
        {
            App.Log("ProfileStore.ClearAvatar FALLÓ", ex);
        }
    }

    public static string Initials(string? name)
    {
        string value = (name ?? string.Empty).Trim();
        if (value.Length == 0) return "AE";

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0].Substring(0, 2).ToUpperInvariant() : parts[0].ToUpperInvariant();

        return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
    }
}
