using System;
using System.IO;
using System.Text.Json;

namespace FrameForge;

public sealed class AppSettings
{
    public string? VideoRuntimePath { get; set; }
}

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    public static void SetVideoRuntimePath(string? runtimePath)
    {
        Current.VideoRuntimePath = string.IsNullOrWhiteSpace(runtimePath) ? null : runtimePath.Trim();
        Save(Current);
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
