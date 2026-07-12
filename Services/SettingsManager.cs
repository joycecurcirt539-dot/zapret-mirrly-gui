using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace ZapretMirrlyGUI.Services;

public class AppSettings
{
    public string LastSelectedPreset { get; set; } = "general.bat";
    public string DiagnosticsTestType { get; set; } = "standard";
    public string DiagnosticsRunMode { get; set; } = "all";
    public string DiagnosticsTimeout { get; set; } = "5";
    public List<string> DiagnosticsSelectedPresets { get; set; } = new();
    public bool MinimizeToTrayOnClose { get; set; } = false;
    public bool MinimizeToTrayOnMinimize { get; set; } = false;
    public bool AskBeforeClosing { get; set; } = true;
}

public static class SettingsManager
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZapretMirrlyGUI"
    );
    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    private static AppSettings _settings = new();

    static SettingsManager()
    {
        Load();
    }

    public static AppSettings Instance => _settings;

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                }
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
