using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services;

public static class TgWsProxyService
{
    private static Process? _runningProcess;
    private static readonly List<string> _logHistory = new();
    private const int MaxLogHistory = 2000;

    public static event Action<string>? OnLogReceived;
    public static event Action<bool>? OnStatusChanged;

    public static bool IsRunning => _runningProcess != null && !_runningProcess.HasExited;

    public static void StartProxy()
    {
        if (IsRunning) StopProxy();

        var tgwsproxyDir = AssetsExtractor.GetTgWsProxyPath();
        var exePath = Path.Combine(tgwsproxyDir, "TgWsProxy_windows.exe");

        if (!File.Exists(exePath))
        {
            Log("[TG_SERVICE ERROR] Исполняемый файл TgWsProxy_windows.exe не найден в директории приложения.");
            return;
        }

        // Write configuration file
        try
        {
            var configJson = $$"""
{
  "port": {{SettingsManager.Instance.TgWsProxyPort}},
  "host": "{{SettingsManager.Instance.TgWsProxyHost}}",
  "dc_ip": [
    "2:149.154.167.220",
    "4:149.154.167.220"
  ],
  "verbose": false,
  "check_updates": false,
  "log_max_mb": 5,
  "buf_kb": 256,
  "pool_size": 4,
  "cfproxy": {{(SettingsManager.Instance.TgWsProxyCfProxy ? "true" : "false")}},
  "cfproxy_user_domain": [],
  "cfproxy_worker_domain": [],
  "ws_keepalive_interval": 30,
  "secret": "{{SettingsManager.Instance.TgWsProxySecret}}",
  "language": "ru",
  "autostart": false
}
""";
            var configPath = Path.Combine(tgwsproxyDir, "config.json");
            File.WriteAllText(configPath, configJson, Encoding.UTF8);
            Log("[TG_SERVICE] Файл конфигурации config.json успешно создан.");
        }
        catch (Exception ex)
        {
            Log($"[TG_SERVICE ERROR] Ошибка записи config.json: {ex.Message}");
            return;
        }

        Log("[TG_SERVICE] Запуск TgWsProxy...");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = tgwsproxyDir,
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
                Log("[TG_SERVICE] Процесс TgWsProxy завершён.");
            };

            _runningProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"[tgwsproxy] {e.Data}");
                }
            };

            _runningProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"[tgwsproxy ERROR] {e.Data}");
                }
            };

            _runningProcess.Start();
            _runningProcess.BeginOutputReadLine();
            _runningProcess.BeginErrorReadLine();

            OnStatusChanged?.Invoke(true);
            Log($"[TG_SERVICE] TgWsProxy успешно запущен (PID: {_runningProcess.Id}).");
        }
        catch (Exception ex)
        {
            Log($"[TG_SERVICE ERROR] Не удалось запустить TgWsProxy: {ex.Message}");
            _runningProcess = null;
            OnStatusChanged?.Invoke(false);
        }
    }

    public static void StopProxy()
    {
        if (_runningProcess != null)
        {
            try
            {
                if (!_runningProcess.HasExited)
                {
                    Log("[TG_SERVICE] Завершение процесса TgWsProxy...");
                    _runningProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Log($"[TG_SERVICE ERROR] Ошибка при завершении процесса: {ex.Message}");
            }
            finally
            {
                _runningProcess.Dispose();
                _runningProcess = null;
                OnStatusChanged?.Invoke(false);
                Log("[TG_SERVICE] Процесс TgWsProxy остановлен.");
            }
        }

        KillAllTgWsProxyProcesses();
    }

    public static void KillAllTgWsProxyProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("TgWsProxy_windows"))
            {
                try
                {
                    proc.Kill(true);
                    Log($"[TG_SERVICE] Принудительно завершен сторонний процесс TgWsProxy_windows (PID: {proc.Id})");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"[TG_SERVICE ERROR] Не удалось завершить процессы TgWsProxy: {ex.Message}");
        }
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
}
