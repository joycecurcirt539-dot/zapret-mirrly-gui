using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ZapretMirrlyGUI.Services.TgWsProxy;

namespace ZapretMirrlyGUI.Services;

public static class TgWsProxyService
{
    private static TgWsProxyServer? _server;
    private static readonly List<string> _logHistory = new();
    private const int MaxLogHistory = 2000;

    public static event Action<string>? OnLogReceived;
    public static event Action<bool>? OnStatusChanged;

    public static bool IsRunning => _server != null;

    public static void StartProxy()
    {
        if (IsRunning) StopProxy();

        Log("[TG_SERVICE] Инициализация встроенного C# TgWsProxy сервера...");

        try
        {
            var dcRedirects = new Dictionary<int, string>
            {
                { 2, "149.154.167.220" },
                { 4, "149.154.167.220" }
            };

            int port = SettingsManager.Instance.TgWsProxyPort;
            string host = SettingsManager.Instance.TgWsProxyHost;
            string secret = SettingsManager.Instance.TgWsProxySecret;
            bool cfProxy = SettingsManager.Instance.TgWsProxyCfProxy;
            string fakeTlsDomain = SettingsManager.Instance.TgWsProxyFakeTlsDomain;
            int poolSize = SettingsManager.Instance.TgWsProxyPoolSize;
            bool forceTestDc = SettingsManager.Instance.TgWsProxyForceTestDc;

            var workerDomains = new List<string>();
            string rawWorkers = SettingsManager.Instance.TgWsProxyWorkerDomains ?? "";
            string[] parts = rawWorkers.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                workerDomains.Add(p.Trim());
            }

            _server = new TgWsProxyServer(
                host,
                port,
                secret,
                dcRedirects,
                cfProxy,
                workerDomains,
                fakeTlsDomain,
                forceTestDc,
                logCallback: Log
            )
            {
                PoolSize = poolSize
            };

            _server.Start();
            OnStatusChanged?.Invoke(true);
            Log("[TG_SERVICE] TgWsProxy успешно запущен в основном процессе GUI.");
        }
        catch (Exception ex)
        {
            Log($"[TG_SERVICE ERROR] Не удалось запустить TgWsProxy: {ex.Message}");
            _server = null;
            OnStatusChanged?.Invoke(false);
        }
    }

    public static void StopProxy()
    {
        if (_server != null)
        {
            try
            {
                Log("[TG_SERVICE] Остановка TgWsProxy...");
                _server.Stop();
            }
            catch (Exception ex)
            {
                Log($"[TG_SERVICE ERROR] Ошибка при остановке прокси: {ex.Message}");
            }
            finally
            {
                _server = null;
                OnStatusChanged?.Invoke(false);
                Log("[TG_SERVICE] Процесс TgWsProxy остановлен.");
            }
        }
    }

    public static void KillAllTgWsProxyProcesses()
    {
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("TgWsProxy_windows"))
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
