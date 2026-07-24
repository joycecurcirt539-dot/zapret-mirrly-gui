using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services;

public static class NetworkMonitorService
{
    private static Timer? _debounceTimer;
    private static bool _isMonitoring = false;
    private static readonly object _lockObj = new();

    public static event Action<string>? OnNetworkConnecting;
    public static event Action<bool, bool, string, string>? OnNetworkResult;

    public static void StartMonitoring()
    {
        lock (_lockObj)
        {
            if (_isMonitoring) return;
            _isMonitoring = true;

            try
            {
                NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
                System.Diagnostics.Debug.WriteLine("[NetworkMonitorService] Started monitoring network changes.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NetworkMonitorService] Error starting listener: {ex.Message}");
            }
        }
    }

    public static void StopMonitoring()
    {
        lock (_lockObj)
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;

            try
            {
                NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NetworkMonitorService] Error stopping listener: {ex.Message}");
            }
        }
    }

    private static void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        lock (_lockObj)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(OnDebouncedNetworkChange, null, 2500, Timeout.Infinite);
        }
    }

    private static async void OnDebouncedNetworkChange(object? state)
    {
        bool dpiWasRunning = ZapretService.IsRunning;
        bool tgWasRunning = TgWsProxyService.IsRunning;

        if (!dpiWasRunning && !tgWasRunning)
        {
            return;
        }

        System.Diagnostics.Debug.WriteLine("[NetworkMonitorService] Network change detected. Restarting active bypasses...");

        // 1. Notify UI that reconnection has started (Spinning ProgressRing + Amber status)
        OnNetworkConnecting?.Invoke("Переподключение...");

        try
        {
            // 2. Restart DPI Bypass if it was running
            if (dpiWasRunning)
            {
                var activePreset = SettingsManager.Instance.LastSelectedPreset;
                var gameFilter = GetActiveGameFilterMode();
                ZapretService.StopBypass();
                await Task.Delay(500);
                ZapretService.StartBypass(activePreset, gameFilter);
            }

            // 3. Restart TG WS Proxy if it was running
            if (tgWasRunning)
            {
                TgWsProxyService.StopProxy();
                await Task.Delay(300);
                TgWsProxyService.StartProxy();
            }

            // 4. Measure real network latency / probe connectivity
            string dpiPingText = "";
            bool dpiSuccess = false;
            if (dpiWasRunning)
            {
                var dpiProbe = await NetworkLatencyService.MeasureHttpSniLatencyAsync("YouTube", "https://www.youtube.com/generate_204", 1500);
                dpiSuccess = dpiProbe.IsSuccess;
                dpiPingText = dpiProbe.FormattedText;
            }

            string tgPingText = "";
            bool tgSuccess = false;
            if (tgWasRunning)
            {
                int tgPort = SettingsManager.Instance.TgWsProxyPort;
                var tgProbe = await NetworkLatencyService.MeasureTgProxyDcLatencyAsync("DC2", tgPort, 1200);
                tgSuccess = tgProbe.IsSuccess;
                tgPingText = tgProbe.FormattedText;
            }

            // 5. Notify UI of final connection & latency results
            OnNetworkResult?.Invoke(dpiSuccess, tgSuccess, dpiPingText, tgPingText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NetworkMonitorService] Error during auto-restart: {ex.Message}");
            OnNetworkResult?.Invoke(false, false, "Ошибка", "Ошибка");
        }
    }

    private static string GetActiveGameFilterMode()
    {
        try
        {
            var root = ZapretService.FindZapretRoot();
            var file = Path.Combine(root, "utils", "game_filter.enabled");
            if (!File.Exists(file)) return "disabled";
            return File.ReadAllText(file).Trim().ToLower();
        }
        catch
        {
            return "disabled";
        }
    }
}
