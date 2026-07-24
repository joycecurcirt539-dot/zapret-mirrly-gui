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

    public static void ExtractEverythingIfNeeded(bool force = false)
    {
        var appDataRoot = GetAppDataRoot();
        if (!Directory.Exists(appDataRoot))
        {
            Directory.CreateDirectory(appDataRoot);
        }

        var versionFile = Path.Combine(appDataRoot, "gui_version.txt");
        var currentVersion = "1.1.6";
        bool versionChanged = true;

        if (File.Exists(versionFile))
        {
            try
            {
                var savedVersion = File.ReadAllText(versionFile).Trim();
                if (savedVersion == currentVersion)
                {
                    versionChanged = false;
                }
            }
            catch { }
        }

        if (versionChanged || force)
        {
            try
            {
                var winwsPath = Path.Combine(GetZapretPath(), "bin", "winws.exe");
                if (File.Exists(winwsPath))
                {
                    File.Delete(winwsPath);
                }
            }
            catch { }

            // Clean up old incorrect extraction paths directly in appDataRoot
            try
            {
                var oldBin = Path.Combine(appDataRoot, "bin");
                if (Directory.Exists(oldBin)) Directory.Delete(oldBin, true);

                var oldLists = Path.Combine(appDataRoot, "lists");
                if (Directory.Exists(oldLists)) Directory.Delete(oldLists, true);

                var oldUtils = Path.Combine(appDataRoot, "utils");
                if (Directory.Exists(oldUtils)) Directory.Delete(oldUtils, true);

                var oldServiceBat = Path.Combine(appDataRoot, "service.bat");
                if (File.Exists(oldServiceBat)) File.Delete(oldServiceBat);

                if (Directory.Exists(appDataRoot))
                {
                    foreach (var file in Directory.GetFiles(appDataRoot, "*.bat"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (var file in Directory.GetFiles(appDataRoot, "general*"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
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
            if (!File.Exists(destPath) || versionChanged || force)
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

        if (!Directory.Exists(zapretDir) || !File.Exists(Path.Combine(zapretDir, "bin", "winws.exe")) || force)
        {
            string resourceName = "ZapretMirrlyGUI.Assets.zapret.zip";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var archive = new ZipArchive(stream);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var destPath = Path.GetFullPath(Path.Combine(zapretDir, entry.FullName));

                    if (!destPath.StartsWith(zapretDir, StringComparison.OrdinalIgnoreCase)) continue;

                    // If it is a list file, only overwrite if it is a system file and version changed
                    if (entry.FullName.StartsWith("lists/", StringComparison.OrdinalIgnoreCase) && File.Exists(destPath))
                    {
                        if (!versionChanged && !force)
                        {
                            continue;
                        }
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

        if (versionChanged || force)
        {
            try
            {
                File.WriteAllText(versionFile, currentVersion);
            }
            catch { }
        }
    }

    public static void CleanAndReinstall()
    {
        // 1. Stop bypass and clean winws/WinDivert instances
        try
        {
            ZapretService.StopBypass();
        }
        catch { }

        // Give OS a little time to release file/driver locks
        System.Threading.Thread.Sleep(800);

        var zapretDir = GetZapretPath();

        // 2. Backup user domain lists
        var listsDir = Path.Combine(zapretDir, "lists");
        var backupListFiles = new System.Collections.Generic.Dictionary<string, string>();
        if (Directory.Exists(listsDir))
        {
            try
            {
                foreach (var file in Directory.GetFiles(listsDir, "*.txt"))
                {
                    var name = Path.GetFileName(file);
                    try
                    {
                        backupListFiles[name] = File.ReadAllText(file);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 3. Delete directory zapret
        try
        {
            if (Directory.Exists(zapretDir))
            {
                Directory.Delete(zapretDir, true);
            }
        }
        catch (Exception ex)
        {
            try
            {
                DeleteDirectoryContentsSafely(zapretDir);
            }
            catch
            {
                throw new Exception($"Не удалось полностью пересоздать папку zapret. Возможно, файлы заблокированы антивирусом. Детали: {ex.Message}", ex);
            }
        }

        // 4. Force extract everything
        ExtractEverythingIfNeeded(force: true);

        // 5. Restore user domain lists
        if (backupListFiles.Count > 0)
        {
            var newListsDir = Path.Combine(zapretDir, "lists");
            if (!Directory.Exists(newListsDir))
            {
                Directory.CreateDirectory(newListsDir);
            }

            foreach (var kvp in backupListFiles)
            {
                try
                {
                    var targetFile = Path.Combine(newListsDir, kvp.Key);
                    File.WriteAllText(targetFile, kvp.Value, System.Text.Encoding.UTF8);
                }
                catch { }
            }
        }
    }

    private static void DeleteDirectoryContentsSafely(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.GetFiles(path))
        {
            try { File.Delete(file); } catch { }
        }
        foreach (var dir in Directory.GetDirectories(path))
        {
            try
            {
                DeleteDirectoryContentsSafely(dir);
                Directory.Delete(dir, true);
            }
            catch { }
        }
    }
}
