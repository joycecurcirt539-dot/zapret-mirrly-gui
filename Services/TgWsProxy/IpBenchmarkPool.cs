using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class IpBenchmarkPool
{
    public record IpMetric(string Ip, long PingMs, DateTime LastTested, bool IsSuccess);

    private static readonly string[] CF_CANDIDATE_IPS = new[]
    {
        "104.16.51.111", "104.16.52.111", "162.159.135.42", "172.67.74.129",
        "104.18.2.16", "104.21.32.1", "188.114.96.1", "162.159.128.1",
        "172.64.144.1", "104.16.132.229", "104.16.133.229", "172.67.182.190",
        "104.19.141.15", "104.19.142.15", "162.159.134.42", "188.114.97.1"
    };

    private static readonly Dictionary<int, string[]> DC_DIRECT_IPS = new()
    {
        { 1, new[] { "149.154.175.50", "149.154.175.51", "149.154.175.100" } },
        { 2, new[] { "149.154.167.220", "149.154.167.50", "149.154.167.99" } },
        { 3, new[] { "149.154.175.100", "149.154.175.117" } },
        { 4, new[] { "149.154.167.220", "149.154.167.91", "149.154.167.151" } },
        { 5, new[] { "91.108.56.130", "91.108.56.165", "91.108.56.170" } },
        { 203, new[] { "149.154.167.220", "149.154.167.50" } }
    };

    private readonly ConcurrentDictionary<string, IpMetric> _metrics = new();
    private Action<string> _logCallback;
    private CancellationTokenSource? _cts;

    public static IpBenchmarkPool Instance { get; } = new IpBenchmarkPool(msg => { });

    public IpBenchmarkPool(Action<string> logCallback)
    {
        _logCallback = logCallback;
    }

    public void Start(Action<string>? logger = null)
    {
        if (logger != null) _logCallback = logger;
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            _logCallback("[IP_BENCHMARK] Запуск службы замера задержек IP-адресов Telegram и Cloudflare...");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RunBenchmarkCycleAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logCallback($"[IP_BENCHMARK ERROR] Ошибка цикла тестирования IP: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public string GetBestTargetIp(int dc, string configuredIp)
    {
        if (!string.IsNullOrEmpty(configuredIp))
        {
            if (_metrics.TryGetValue(configuredIp, out var m) && m.IsSuccess)
            {
                return configuredIp;
            }
        }

        if (DC_DIRECT_IPS.TryGetValue(dc, out var candidateIps))
        {
            var bestDcIp = candidateIps
                .Where(ip => _metrics.TryGetValue(ip, out var metric) && metric.IsSuccess)
                .OrderBy(ip => _metrics[ip].PingMs)
                .FirstOrDefault();

            if (bestDcIp != null)
                return bestDcIp;
        }

        var bestCfIp = CF_CANDIDATE_IPS
            .Where(ip => _metrics.TryGetValue(ip, out var metric) && metric.IsSuccess)
            .OrderBy(ip => _metrics[ip].PingMs)
            .FirstOrDefault();

        if (bestCfIp != null)
            return bestCfIp;

        return configuredIp;
    }

    public async Task RunBenchmarkCycleAsync(CancellationToken token)
    {
        var allIps = new HashSet<string>(CF_CANDIDATE_IPS);
        foreach (var arr in DC_DIRECT_IPS.Values)
        {
            foreach (var ip in arr)
                allIps.Add(ip);
        }

        var tasks = allIps.Select(ip => TestIpLatencyAsync(ip, token)).ToList();
        var results = await Task.WhenAll(tasks);

        int totalSuccess = 0;
        long minPing = long.MaxValue;
        string fastestIp = "";

        foreach (var r in results)
        {
            _metrics[r.Ip] = r;
            if (r.IsSuccess)
            {
                totalSuccess++;
                if (r.PingMs < minPing)
                {
                    minPing = r.PingMs;
                    fastestIp = r.Ip;
                }
            }
        }

        if (totalSuccess > 0)
        {
            _logCallback($"[IP_BENCHMARK] Оттестировано {allIps.Count} IP: {totalSuccess} доступны. Быстрейший: {fastestIp} ({minPing} мс).");
        }
    }

    private static async Task<IpMetric> TestIpLatencyAsync(string ip, CancellationToken token)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(2500);

            await tcp.ConnectAsync(ip, 443, cts.Token);
            sw.Stop();
            return new IpMetric(ip, sw.ElapsedMilliseconds, DateTime.UtcNow, true);
        }
        catch
        {
            sw.Stop();
            return new IpMetric(ip, 9999, DateTime.UtcNow, false);
        }
    }
}
