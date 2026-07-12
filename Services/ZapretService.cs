using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services;

public static class ZapretService
{
    private static Process? _runningProcess;
    private static readonly List<string> _logHistory = new();
    private const int MaxLogHistory = 2000;
    
    public static event Action<string>? OnLogReceived;
    public static event Action<bool>? OnStatusChanged;

    public static bool IsRunning => _runningProcess != null && !_runningProcess.HasExited;
    public static bool IsDiagnosticsRunning { get; set; } = false;

    // ── Elevation helpers ──────────────────────────────────────────────────────
    // The GUI runs as asInvoker (normal user). Privileged child processes
    // (winws.exe, sc.exe) are launched via ShellExecuteEx with verb "runas".
    // This avoids UIPI that would block PrintScreen when GUI itself ran as admin.

    private static string RunElevated(string fileName, string arguments, int timeoutMs = 15000)
    {
        var tmpOut = Path.GetTempFileName();
        try
        {
            // Wrap in cmd /c so we can redirect output to a temp file
            var cmdArgs = $"/c \"{fileName}\" {arguments} > \"{tmpOut}\" 2>&1";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "[ERROR] Не удалось запустить процесс";
            proc.WaitForExit(timeoutMs);
            if (File.Exists(tmpOut))
                return File.ReadAllText(tmpOut, Encoding.UTF8).Trim();
            return "";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.Message}";
        }
        finally
        {
            try { File.Delete(tmpOut); } catch { }
        }
    }

    public static string FindZapretRoot()
    {
        var exePath = Environment.ProcessPath;
        var exeDir = string.IsNullOrEmpty(exePath) ? AppContext.BaseDirectory : Path.GetDirectoryName(exePath);

        // 1. Check next to the EXE (for debug / custom override)
        if (!string.IsNullOrEmpty(exeDir))
        {
            var localCandidate = Path.Combine(exeDir, "zapret");
            if (Directory.Exists(localCandidate))
            {
                return localCandidate;
            }
        }

        // 2. Check in LocalAppData (for embedded self-extracted version)
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretMirrlyGUI", "zapret");
        if (Directory.Exists(appDataPath))
        {
            return appDataPath;
        }

        // 3. Fallback to searching parent directories of the running process path
        var current = exeDir;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "zapret");
            if (Directory.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }

        var fallback = exeDir ?? AppContext.BaseDirectory;
        return Path.Combine(fallback, "zapret");
    }

    public static List<string> GetPresets()
    {
        var root = FindZapretRoot();
        if (!Directory.Exists(root)) return new List<string>();
        return Directory.GetFiles(root, "*.bat")
            .Select(Path.GetFileName)
            .Where(f => f != null && !f.StartsWith("service", StringComparison.OrdinalIgnoreCase))
            .Select(f => f!)
            .OrderBy(f => f)
            .ToList();
    }

    public static string ParseArgumentsFromBatch(string batchFilename, string gameFilterMode)
    {
        var root = FindZapretRoot();
        var batchPath = Path.Combine(root, batchFilename);
        if (!File.Exists(batchPath)) return "";

        var lines = File.ReadAllLines(batchPath, Encoding.UTF8);
        var fullCommandLine = new StringBuilder();
        bool capture = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Contains("winws.exe", StringComparison.OrdinalIgnoreCase))
            {
                capture = true;
            }

            if (capture)
            {
                var cleanLine = line;
                bool hasContinuation = false;
                if (cleanLine.EndsWith("^"))
                {
                    cleanLine = cleanLine.Substring(0, cleanLine.Length - 1).Trim();
                    hasContinuation = true;
                }

                fullCommandLine.Append(" ").Append(cleanLine);

                if (!hasContinuation)
                {
                    break;
                }
            }
        }

        var command = fullCommandLine.ToString();
        var winwsIdx = command.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
        if (winwsIdx == -1) return "";

        var argsPart = command.Substring(winwsIdx + "winws.exe".Length).Trim();
        if (argsPart.StartsWith("\""))
        {
            argsPart = argsPart.Substring(1).Trim();
        }
        if (argsPart.EndsWith("\""))
        {
            argsPart = argsPart.Substring(0, argsPart.Length - 1).Trim();
        }

        var binPath = Path.Combine(root, "bin") + "\\";
        var listsPath = Path.Combine(root, "lists") + "\\";

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

        argsPart = argsPart.Replace("%BIN%", binPath, StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%LISTS%", listsPath, StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%GameFilterTCP%", gameFilterTCP, StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%GameFilterUDP%", gameFilterUDP, StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%~dp0", root + "\\", StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%%BIN%%", binPath, StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%%LISTS%%", listsPath, StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%%GameFilterTCP%%", gameFilterTCP, StringComparison.OrdinalIgnoreCase);
        argsPart = argsPart.Replace("%%GameFilterUDP%%", gameFilterUDP, StringComparison.OrdinalIgnoreCase);

        // Replace caret symbols that batch uses to escape things
        argsPart = argsPart.Replace("^", "");

        return argsPart.Trim();
    }

    public static void StartBypass(string batchFilename, string gameFilterMode)
    {
        if (IsRunning) StopBypass();

        // Enable TCP Timestamps automatically
        EnableTcpTimestamps();

        var root = FindZapretRoot();
        var winwsPath = Path.Combine(root, "bin", "winws.exe");
        var args = ParseArgumentsFromBatch(batchFilename, gameFilterMode);

        Log($"[SERVICE] Запуск {batchFilename}...");
        Log($"[CMD] {winwsPath} {args}");

        var startInfo = new ProcessStartInfo
        {
            FileName = winwsPath,
            Arguments = args,
            WorkingDirectory = Path.Combine(root, "bin"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            _runningProcess = new Process { StartInfo = startInfo };
            _runningProcess.EnableRaisingEvents = true;
            _runningProcess.Exited += (s, e) =>
            {
                OnStatusChanged?.Invoke(false);
                Log("[SERVICE] Процесс winws.exe завершён.");
            };

            _runningProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"[winws] {e.Data}");
                }
            };

            _runningProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"[winws ERROR] {e.Data}");
                }
            };

            _runningProcess.Start();
            _runningProcess.BeginOutputReadLine();
            _runningProcess.BeginErrorReadLine();

            OnStatusChanged?.Invoke(true);
            Log($"[SERVICE] winws.exe успешно запущен (PID: {_runningProcess.Id}).");
        }
        catch (Exception ex)
        {
            Log($"[SERVICE ERROR] Не удалось запустить winws.exe: {ex.Message}");
            _runningProcess = null;
            OnStatusChanged?.Invoke(false);
        }
    }

    public static void StopBypass()
    {
        if (_runningProcess != null)
        {
            try
            {
                if (!_runningProcess.HasExited)
                {
                    Log("[SERVICE] Завершение процесса winws.exe...");
                    _runningProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Log($"[SERVICE ERROR] Ошибка при завершении процесса: {ex.Message}");
            }
            finally
            {
                _runningProcess.Dispose();
                _runningProcess = null;
                OnStatusChanged?.Invoke(false);
                Log("[SERVICE] Процесс winws.exe завершен.");
            }
        }

        // Also clean up any orphan processes
        KillAllWinwsProcesses();
    }




    public static void KillAllWinwsProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("winws"))
            {
                try
                {
                    proc.Kill(true);
                    Log($"[SERVICE] Принудительно завершен сторонний процесс winws.exe (PID: {proc.Id})");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"[SERVICE ERROR] Не удалось завершить процессы winws.exe: {ex.Message}");
        }
    }

    public static string ExecuteCommand(string fileName, string arguments)
    {
        // sc.exe and similar tools require elevation — use runas via RunElevated.
        // For read-only queries that don't strictly need admin, we try directly first
        // and fall back to elevated if we get access denied.
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = Process.Start(startInfo);
            if (process == null) return RunElevated(fileName, arguments);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            // If access denied, retry elevated
            if (process.ExitCode == 5 || output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
                return RunElevated(fileName, arguments);
            return string.IsNullOrWhiteSpace(error) ? output : $"{output}\nError: {error}";
        }
        catch (Exception ex)
        {
            return $"Исключение при вызове {fileName}: {ex.Message}";
        }
    }

    public static bool IsServiceInstalled()
    {
        var output = ExecuteCommand("sc.exe", "query zapret");
        return !output.Contains("FAILED 1060", StringComparison.OrdinalIgnoreCase) && 
               !output.Contains("не установлена", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetServiceStatus()
    {
        var output = ExecuteCommand("sc.exe", "query zapret");
        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return "RUNNING";
        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            return "STOPPED";
        if (output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase))
            return "START_PENDING";
        if (output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))
            return "STOP_PENDING";
        if (output.Contains("FAILED 1060", StringComparison.OrdinalIgnoreCase) || 
            output.Contains("не установлена", StringComparison.OrdinalIgnoreCase))
            return "NOT_INSTALLED";
        return "UNKNOWN";
    }

    public static string InstallService(string batchFilename, string gameFilterMode)
    {
        Log("[SERVICE] Остановка и удаление предыдущей службы zapret...");
        RemoveService();

        // Enable TCP Timestamps automatically
        EnableTcpTimestamps();

        var root = FindZapretRoot();
        var winwsPath = Path.Combine(root, "bin", "winws.exe");
        var args = ParseArgumentsFromBatch(batchFilename, gameFilterMode);

        // Escape double quotes inside the binPath parameter of sc.exe
        var binPathArg = $"\"\\\"{winwsPath}\\\" {args}\"";

        Log($"[SERVICE] Установка службы: sc.exe create zapret binPath= {binPathArg} DisplayName= \"zapret\" start= auto");
        var createResult = ExecuteCommand("sc.exe", $"create zapret binPath= {binPathArg} DisplayName= \"zapret\" start= auto");
        Log(createResult);

        if (createResult.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteCommand("sc.exe", "description zapret \"Zapret DPI bypass software\"");
            
            // Set registry key so other tools know which strategy was active
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\zapret", true);
                if (key != null)
                {
                    var strategyName = Path.GetFileNameWithoutExtension(batchFilename);
                    key.SetValue("zapret-discord-youtube", strategyName);
                }
            }
            catch (Exception ex)
            {
                Log($"[SERVICE WARNING] Не удалось записать имя стратегии в реестр: {ex.Message}");
            }

            Log("[SERVICE] Запуск службы zapret...");
            var startResult = ExecuteCommand("sc.exe", "start zapret");
            Log(startResult);
            return "Служба успешно установлена и запущена.";
        }

        return "Ошибка при установке службы: " + createResult;
    }

    public static string RemoveService()
    {
        Log("[SERVICE] Остановка службы zapret...");
        var stopResult = ExecuteCommand("sc.exe", "stop zapret");
        Log(stopResult);
        
        Log("[SERVICE] Удаление службы zapret...");
        var deleteResult = ExecuteCommand("sc.exe", "delete zapret");
        Log(deleteResult);

        // Remove WinDivert service driver blocks
        ExecuteCommand("sc.exe", "stop WinDivert");
        ExecuteCommand("sc.exe", "delete WinDivert");
        ExecuteCommand("sc.exe", "stop WinDivert14");
        ExecuteCommand("sc.exe", "delete WinDivert14");

        KillAllWinwsProcesses();

        return "Служба zapret удалена.";
    }

    private static void Log(string message)
    {
        var formatted = $"{DateTime.Now:HH:mm:ss} {message}";
        lock (_logHistory)
        {
            _logHistory.Add(formatted);
            if (_logHistory.Count > MaxLogHistory) _logHistory.RemoveAt(0);
        }
        OnLogReceived?.Invoke(formatted);
    }

    public static IReadOnlyList<string> GetLogHistory()
    {
        lock (_logHistory)
        {
            return _logHistory.ToArray();
        }
    }

    // ─── Missing CLI Parity Features ──────────────────────────────────────────

    public static void EnableTcpTimestamps()
    {
        try
        {
            Log("[SERVICE] Проверка TCP timestamps...");
            var check = ExecuteCommand("netsh.exe", "interface tcp show global");
            if (!check.Contains("timestamps", StringComparison.OrdinalIgnoreCase) || 
                !check.Contains("enabled", StringComparison.OrdinalIgnoreCase))
            {
                Log("[SERVICE] TCP timestamps отключены. Включение...");
                var res = RunElevated("netsh.exe", "interface tcp set global timestamps=enabled");
                Log($"[SERVICE] Результат настройки TCP timestamps: {res}");
            }
            else
            {
                Log("[SERVICE] TCP timestamps уже включены в системе.");
            }
        }
        catch (Exception ex)
        {
            Log($"[SERVICE ERROR] Не удалось настроить TCP timestamps: {ex.Message}");
        }
    }

    public static async Task<bool> UpdateIpsetListAsync()
    {
        var root = FindZapretRoot();
        var ipsetFile = Path.Combine(root, "lists", "ipset-all.txt");
        var backupFile = ipsetFile + ".backup";
        var url = "https://raw.githubusercontent.com/joycecurcirt539-dot/zapret-mirrly-gui/refs/heads/main/.service/ipset-service.txt";
        
        try
        {
            Log("[SERVICE] Скачивание ipset-all.txt с сервера обновлений...");
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Directory.CreateDirectory(Path.GetDirectoryName(ipsetFile)!);
                await File.WriteAllTextAsync(ipsetFile, content, Encoding.UTF8);
                
                // Also update backup file
                if (File.Exists(backupFile)) File.Delete(backupFile);
                File.Copy(ipsetFile, backupFile);
                
                Log("[SERVICE] Список ipset-all.txt успешно обновлен с удаленного сервера.");
                return true;
            }
            else
            {
                Log($"[SERVICE ERROR] Ошибка при скачивании ipset (HTTP {(int)response.StatusCode}). Пробуем восстановить из локальной копии...");
                if (File.Exists(backupFile))
                {
                    File.Copy(backupFile, ipsetFile, overwrite: true);
                    Log("[SERVICE] Список ipset-all.txt восстановлен из локального резервного файла (backup).");
                    return true;
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"[SERVICE ERROR] Ошибка сети при обновлении ipset: {ex.Message}. Пробуем восстановить из локальной копии...");
            if (File.Exists(backupFile))
            {
                File.Copy(backupFile, ipsetFile, overwrite: true);
                Log("[SERVICE] Список ipset-all.txt восстановлен из локального резервного файла (backup).");
                return true;
            }
            return false;
        }
    }

    public static async Task<(bool NeedsUpdate, string CurrentStatus, string? DownloadedContent)> CheckHostsStatusAsync()
    {
        var hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        var url = "https://raw.githubusercontent.com/joycecurcirt539-dot/zapret-mirrly-gui/refs/heads/main/.service/hosts";

        try
        {
            if (!File.Exists(hostsFile))
            {
                return (true, "Файл hosts не найден", null);
            }

            string remoteContent = "";
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    remoteContent = await response.Content.ReadAsStringAsync();
                }
            }
            catch { }

            // If empty (e.g. download failed or 404), use clean comment headers
            if (string.IsNullOrEmpty(remoteContent))
            {
                remoteContent = "\r\n# zapret-discord-youtube start\r\n# zapret-discord-youtube end\r\n";
            }

            var remoteLines = remoteContent.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries)
                                           .Select(l => l.Trim())
                                           .Where(l => !string.IsNullOrEmpty(l))
                                           .ToList();

            if (remoteLines.Count == 0)
            {
                return (false, "Файл hosts чист", remoteContent);
            }

            var firstLine = remoteLines.First();
            var lastLine = remoteLines.Last();

            var localContent = await File.ReadAllTextAsync(hostsFile, Encoding.UTF8);

            bool hasFirst = localContent.Contains(firstLine, StringComparison.OrdinalIgnoreCase);
            bool hasLast = localContent.Contains(lastLine, StringComparison.OrdinalIgnoreCase);

            if (hasFirst && hasLast)
            {
                return (false, "Файл hosts в актуальном состоянии", remoteContent);
            }
            else
            {
                return (true, "В файле hosts отсутствуют записи обхода", remoteContent);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка проверки hosts: {ex.Message}", null);
        }
    }

    public static async Task<bool> ApplyHostsUpdateAsync(string? content)
    {
        var hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        
        try
        {
            Log("[SERVICE] Обновление файла hosts...");

            if (string.IsNullOrEmpty(content))
            {
                content = "\r\n# zapret-discord-youtube start\r\n# zapret-discord-youtube end\r\n";
            }

            var tempFile = Path.GetTempFileName();
            var localContent = File.Exists(hostsFile) ? await File.ReadAllTextAsync(hostsFile, Encoding.UTF8) : "";
            
            // Strip any existing zapret section
            var pattern = @"#\s*zapret-discord-youtube\s*start.*#\s*zapret-discord-youtube\s*end";
            var cleanContent = System.Text.RegularExpressions.Regex.Replace(localContent, pattern, "", 
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            var newHostsContent = cleanContent + "\r\n\r\n" + content.Trim() + "\r\n";
            await File.WriteAllTextAsync(tempFile, newHostsContent, Encoding.UTF8);

            // Copy to hosts file using elevated command
            var arguments = $"/c copy /y \"{tempFile}\" \"{hostsFile}\"";
            var res = RunElevated("cmd.exe", arguments);
            
            try { File.Delete(tempFile); } catch { }

            Log($"[SERVICE] Результат обновления hosts: {res}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[SERVICE ERROR] Исключение при обновлении hosts: {ex.Message}");
            return false;
        }
    }

    public static async Task<(bool UpdateAvailable, string CurrentVersion, string LatestVersion, string DownloadUrl, string StatusText)> CheckForUpdatesAsync()
    {
        const string currentVersion = "1.9.9a";
        const string fallbackUrl = "https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/";
        const string versionUrl = "https://raw.githubusercontent.com/joycecurcirt539-dot/zapret-mirrly-gui/main/.service/version.txt";

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var response = await client.GetAsync(versionUrl);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, currentVersion, currentVersion, fallbackUrl, "Репозиторий обновлений временно недоступен. Дополнительных обновлений не найдено.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (false, currentVersion, currentVersion, fallbackUrl, $"Ошибка при соединении с сервером обновлений: {(int)response.StatusCode}");
            }

            var latestVersion = (await response.Content.ReadAsStringAsync()).Trim();
            if (string.IsNullOrEmpty(latestVersion))
            {
                return (false, currentVersion, currentVersion, fallbackUrl, "Получен пустой ответ от сервера обновлений.");
            }

            if (latestVersion != currentVersion)
            {
                return (true, currentVersion, latestVersion, "https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases/latest", $"Доступна новая версия: {latestVersion}");
            }

            return (false, currentVersion, currentVersion, fallbackUrl, "Вы используете последнюю версию программы.");
        }
        catch (Exception ex)
        {
            return (false, currentVersion, currentVersion, fallbackUrl, $"Не удалось проверить обновления: {ex.Message}");
        }
    }
}
