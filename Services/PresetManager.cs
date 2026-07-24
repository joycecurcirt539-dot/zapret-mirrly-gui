using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretMirrlyGUI.Services;

public static class PresetManager
{
    /// <summary>
    /// Ensures system exclusion list files exist in zapret/lists directory to prevent winws.exe crashes.
    /// </summary>
    public static void EnsureUserListsExist()
    {
        try
        {
            var listsDir = ZapretService.FindListsDirectory();
            Directory.CreateDirectory(listsDir);

            var ipsetExc = Path.Combine(listsDir, "ipset-exclude.txt");
            if (!File.Exists(ipsetExc))
            {
                File.WriteAllText(ipsetExc, "203.0.113.113/32\r\n", Encoding.UTF8);
            }

            var listExc = Path.Combine(listsDir, "list-exclude.txt");
            if (!File.Exists(listExc))
            {
                File.WriteAllText(listExc, "domain.example.abc\r\n", Encoding.UTF8);
            }

            var ipsetExcUser = Path.Combine(listsDir, "ipset-exclude-user.txt");
            if (!File.Exists(ipsetExcUser))
            {
                File.WriteAllText(ipsetExcUser, "203.0.113.113/32\r\n", Encoding.UTF8);
            }

            var listGenUser = Path.Combine(listsDir, "list-general-user.txt");
            if (!File.Exists(listGenUser))
            {
                File.WriteAllText(listGenUser, "# Never leave this file empty\r\ndomain.example.abc\r\n", Encoding.UTF8);
            }

            var listExcUser = Path.Combine(listsDir, "list-exclude-user.txt");
            if (!File.Exists(listExcUser))
            {
                File.WriteAllText(listExcUser, "domain.example.abc\r\n", Encoding.UTF8);
            }

            var root = ZapretService.FindZapretRoot();
            var serviceBatPath = Path.Combine(root, "service.bat");
            if (!File.Exists(serviceBatPath))
            {
                var flowsealBackup = Path.Combine(root, "..", "GITIGNORE", "zapret-discord-youtube-flowseal", "service.bat");
                if (File.Exists(flowsealBackup))
                {
                    File.Copy(flowsealBackup, serviceBatPath, overwrite: true);
                }
            }
        }
        catch { }
    }

    private static int GetPresetSortOrder(string filename, out int numericVal)
    {
        numericVal = int.MaxValue;

        // Group 1: general (ALT) or general (ALT<N>)
        var matchAlt = Regex.Match(filename, @"^general\s*\(ALT(\d+)?\)\.bat$", RegexOptions.IgnoreCase);
        if (matchAlt.Success)
        {
            var numStr = matchAlt.Groups[1].Value;
            numericVal = string.IsNullOrEmpty(numStr) ? 1 : int.Parse(numStr);
            return 1;
        }

        // Group 2: general (FAKE TLS AUTO).bat
        if (filename.Equals("general (FAKE TLS AUTO).bat", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        // Group 3: general (FAKE TLS AUTO ALT) or general (FAKE TLS AUTO ALT<N>)
        var matchFakeTls = Regex.Match(filename, @"^general\s*\(FAKE\s*TLS\s*AUTO\s*ALT(\d+)?\)\.bat$", RegexOptions.IgnoreCase);
        if (matchFakeTls.Success)
        {
            var numStr = matchFakeTls.Groups[1].Value;
            numericVal = string.IsNullOrEmpty(numStr) ? 1 : int.Parse(numStr);
            return 3;
        }

        // Group 4: general (SIMPLE FAKE).bat
        if (filename.Equals("general (SIMPLE FAKE).bat", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        // Group 5: general (SIMPLE FAKE ALT) or general (SIMPLE FAKE ALT<N>)
        var matchSimpleFake = Regex.Match(filename, @"^general\s*\(SIMPLE\s*FAKE\s*ALT(\d+)?\)\.bat$", RegexOptions.IgnoreCase);
        if (matchSimpleFake.Success)
        {
            var numStr = matchSimpleFake.Groups[1].Value;
            numericVal = string.IsNullOrEmpty(numStr) ? 1 : int.Parse(numStr);
            return 5;
        }

        // Group 6: general.bat
        if (filename.Equals("general.bat", StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue - 3;
        }

        return int.MaxValue - 1;
    }

    /// <summary>
    /// Gets all available Flowseal .bat preset files in the zapret root directory.
    /// </summary>
    public static List<string> GetAvailablePresets()
    {
        EnsureUserListsExist();

        var root = ZapretService.FindZapretRoot();
        if (!Directory.Exists(root)) return new List<string>();

        return Directory.GetFiles(root, "*.bat")
            .Select(Path.GetFileName)
            .Where(f => f != null && !f.StartsWith("service", StringComparison.OrdinalIgnoreCase))
            .Select(f => f!)
            .OrderBy(f => {
                int num;
                int group = GetPresetSortOrder(f, out num);
                return (group, num, f.ToLowerInvariant());
            })
            .ToList();
    }

    public static string ParsePresetArguments(string batchFilename)
    {
        EnsureUserListsExist();

        var root = ZapretService.FindZapretRoot();
        var batchPath = Path.Combine(root, batchFilename);
        if (!File.Exists(batchPath)) return "";

        try
        {
            var lines = File.ReadAllLines(batchPath, Encoding.UTF8);
            var sb = new StringBuilder();
            bool capture = false;

            var binPath = Path.Combine(root, "bin") + "\\";
            var listsPath = Path.Combine(root, "lists") + "\\";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Skip comments and empty lines
                if (line.StartsWith("rem", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("::") ||
                    line.StartsWith("@echo", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("chcp", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("call service.bat", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (line.Contains("winws.exe", StringComparison.OrdinalIgnoreCase))
                {
                    capture = true;
                    // Extract part after winws.exe
                    int idx = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                    line = line.Substring(idx + 9).Trim();
                    if (line.StartsWith("\""))
                    {
                        line = line.Substring(1).Trim();
                    }
                }

                if (capture)
                {
                    // Handle batch line continuation (caret ^ at end of line)
                    bool hasContinuation = line.EndsWith("^");
                    if (hasContinuation)
                    {
                        line = line.Substring(0, line.Length - 1).Trim();
                    }

                    if (!string.IsNullOrEmpty(line))
                    {
                        sb.Append(" ").Append(line);
                    }

                    if (!hasContinuation && sb.Length > 0)
                    {
                        // End of command line
                        break;
                    }
                }
            }

            var result = sb.ToString().Trim();

            // Replace Batch environment variables with actual local paths
            result = Regex.Replace(result, @"%~dp0bin\\", binPath, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"%~dp0lists\\", listsPath, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"%~dp0", root + "\\", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"%BIN%", binPath, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"%LISTS%", listsPath, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"%LISTS_DIR%", listsPath, RegexOptions.IgnoreCase);

            // Replace caret symbols used in batch file escaping
            result = result.Replace("^", "");

            return result.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PRESET ERROR] Не удалось распарсить пресет {batchFilename}: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Dynamically applies user settings (Game Filter, Bind Interface) 
    /// on top of the Flowseal preset arguments without touching the original .bat file.
    /// </summary>
    public static string BuildFinalWinwsArguments(string batchFilename, string gameFilterMode)
    {
        var baseArgs = ParsePresetArguments(batchFilename);
        if (string.IsNullOrWhiteSpace(baseArgs)) return "";

        string gameFilterTCP = "12";
        string gameFilterUDP = "12";
        if (gameFilterMode == "all")
        {
            gameFilterTCP = "1024-65535";
            gameFilterUDP = "1024-65535";
        }
        else if (gameFilterMode == "tcp")
        {
            gameFilterTCP = "1024-65535";
            gameFilterUDP = "12";
        }
        else if (gameFilterMode == "udp")
        {
            gameFilterTCP = "12";
            gameFilterUDP = "1024-65535";
        }

        baseArgs = Regex.Replace(baseArgs, @"%GameFilterTCP%", gameFilterTCP, RegexOptions.IgnoreCase);
        baseArgs = Regex.Replace(baseArgs, @"%GameFilterUDP%", gameFilterUDP, RegexOptions.IgnoreCase);
        baseArgs = Regex.Replace(baseArgs, @"%%GameFilterTCP%%", gameFilterTCP, RegexOptions.IgnoreCase);
        baseArgs = Regex.Replace(baseArgs, @"%%GameFilterUDP%%", gameFilterUDP, RegexOptions.IgnoreCase);

        var settings = SettingsManager.Instance;
        var argsBuilder = new StringBuilder(baseArgs);

        // Network Interface binding
        if (!string.IsNullOrWhiteSpace(settings.BindInterface) && settings.BindInterface != "default")
        {
            argsBuilder.Append($" --ifname=\"{settings.BindInterface}\"");
        }

        // Custom User Winws Arguments
        var customArgs = settings.CustomWinwsArgs;
        if (!string.IsNullOrWhiteSpace(customArgs))
        {
            argsBuilder.Append(" ").Append(customArgs.Trim());
        }

        return argsBuilder.ToString().Trim();
    }
}
