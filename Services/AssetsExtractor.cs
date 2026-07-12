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
        // If the directory does not exist, or if winws.exe is missing (e.g. incomplete extraction)
        if (!Directory.Exists(zapretDir) || !File.Exists(Path.Combine(zapretDir, "bin", "winws.exe")))
        {
            if (Directory.Exists(zapretDir))
            {
                try { Directory.Delete(zapretDir, true); } catch { }
            }

            string resourceName = "ZapretMirrlyGUI.Assets.zapret.zip";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var archive = new ZipArchive(stream);
                archive.ExtractToDirectory(appDataRoot, true);
            }
        }
    }
}
