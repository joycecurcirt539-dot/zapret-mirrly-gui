using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace ZapretMirrlyGUI.Services;

public static class AssetsExtractor
{
    public static string GetAppDataRoot()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretMirrlyGUI");
    }

    public static string GetAssetsPath()
    {
        return Path.Combine(GetAppDataRoot(), "Assets");
    }

    public static string GetZapretPath()
    {
        return Path.Combine(GetAppDataRoot(), "zapret");
    }

    public static void ExtractEverythingIfNeeded()
    {
        var appDataRoot = GetAppDataRoot();
        if (!Directory.Exists(appDataRoot))
        {
            Directory.CreateDirectory(appDataRoot);
        }

        var assembly = Assembly.GetExecutingAssembly();

        // 1. Extract Assets
        var assetsDir = GetAssetsPath();
        if (!Directory.Exists(assetsDir))
        {
            Directory.CreateDirectory(assetsDir);
        }

        string[] assets = { "AppIcon.ico", "SidebarLogoNav.png", "dalink-qr-code.png" };
        foreach (var asset in assets)
        {
            string destPath = Path.Combine(assetsDir, asset);
            if (!File.Exists(destPath))
            {
                string resourceName = $"ZapretMirrlyGUI.Assets.{asset}";
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var fileStream = File.Create(destPath);
                    stream.CopyTo(fileStream);
                }
            }
        }

        // 2. Extract Zapret zip archive
        var zapretDir = GetZapretPath();
        bool needsUpdate = false;

        var scriptPath = Path.Combine(zapretDir, "utils", "test zapret.ps1");
        if (File.Exists(scriptPath))
        {
            try
            {
                var content = File.ReadAllText(scriptPath);
                if (!content.Contains("# GUI_COMPATIBLE_V2"))
                {
                    needsUpdate = true;
                }
            }
            catch
            {
                needsUpdate = true;
            }
        }
        else
        {
            needsUpdate = true;
        }

        if (!Directory.Exists(zapretDir) || !File.Exists(Path.Combine(zapretDir, "bin", "winws.exe")) || needsUpdate)
        {
            string resourceName = "ZapretMirrlyGUI.Assets.zapret.zip";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var archive = new ZipArchive(stream);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var destPath = Path.GetFullPath(Path.Combine(appDataRoot, entry.FullName));

                    if (!destPath.StartsWith(appDataRoot, StringComparison.OrdinalIgnoreCase)) continue;

                    if (entry.FullName.StartsWith("zapret/lists/", StringComparison.OrdinalIgnoreCase) && File.Exists(destPath))
                    {
                        continue;
                    }

                    var parentDir = Path.GetDirectoryName(destPath);
                    if (parentDir != null && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    try
                    {
                        entry.ExtractToFile(destPath, true);
                    }
                    catch { }
                }
            }
        }
    }
}
