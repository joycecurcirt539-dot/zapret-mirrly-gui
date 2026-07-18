using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class WsPool
{
    private const double WS_POOL_MAX_AGE = 120.0;
    private readonly ConcurrentDictionary<(int Dc, bool IsMedia), ConcurrentQueue<(RawWebSocket Ws, DateTime Created)>> _idle = new();
    private readonly ConcurrentDictionary<(int Dc, bool IsMedia), bool> _refilling = new();
    private readonly Action<string> _logCallback;

    public DateTime FrontingUntil { get; set; } = DateTime.MinValue;
    public int PoolSize { get; set; } = 4;

    public WsPool(Action<string> logCallback)
    {
        _logCallback = logCallback;
    }

    public async Task<RawWebSocket?> GetAsync(int dc, bool isMedia, string targetIp, List<string> domains)
    {
        var key = (dc, isMedia);
        var bucket = _idle.GetOrAdd(key, _ => new ConcurrentQueue<(RawWebSocket Ws, DateTime Created)>());
        
        while (bucket.TryDequeue(out var item))
        {
            double age = (DateTime.UtcNow - item.Created).TotalSeconds;
            if (age > WS_POOL_MAX_AGE || item.Ws.IsClosed)
            {
                _ = QuietCloseAsync(item.Ws);
                continue;
            }

            _logCallback($"[WsPool] Pool hit for DC{dc}{(isMedia ? "m" : "")} (age={age:F1}s, left={bucket.Count})");
            ScheduleRefill(key, targetIp, domains);
            return item.Ws;
        }

        ScheduleRefill(key, targetIp, domains);
        return null;
    }

    private void ScheduleRefill((int Dc, bool IsMedia) key, string targetIp, List<string> domains)
    {
        if (_refilling.TryAdd(key, true))
        {
            Task.Run(async () =>
            {
                try
                {
                    await RefillAsync(key, targetIp, domains);
                }
                finally
                {
                    _refilling.TryRemove(key, out _);
                }
            });
        }
    }

    private async Task RefillAsync((int Dc, bool IsMedia) key, string targetIp, List<string> domains)
    {
        var bucket = _idle.GetOrAdd(key, _ => new ConcurrentQueue<(RawWebSocket Ws, DateTime Created)>());
        int needed = PoolSize - bucket.Count;
        if (needed <= 0)
            return;

        var tasks = new List<Task<RawWebSocket?>>();
        bool isFronting = DateTime.UtcNow < FrontingUntil;
        for (int i = 0; i < needed; i++)
        {
            tasks.Add(ConnectOneAsync(targetIp, domains, isFronting));
        }

        var results = await Task.WhenAll(tasks);
        int added = 0;
        foreach (var ws in results)
        {
            if (ws != null)
            {
                bucket.Enqueue((ws, DateTime.UtcNow));
                added++;
            }
        }

        if (added > 0)
        {
            _logCallback($"[WsPool] Refilled DC{key.Dc}{(key.IsMedia ? "m" : "")}: {bucket.Count} ready.");
        }
    }

    private async Task<RawWebSocket?> ConnectOneAsync(string targetIp, List<string> domains, bool isFronting)
    {
        foreach (var domain in domains)
        {
            try
            {
                string? sni = isFronting ? "sprinthost.ru" : domain;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                return await RawWebSocket.ConnectAsync(targetIp, domain, "/apiws", sni, cts.Token);
            }
            catch
            {
            }
        }
        return null;
    }

    private static async Task QuietCloseAsync(RawWebSocket ws)
    {
        try
        {
            await ws.CloseAsync();
        }
        catch { }
    }

    public void Warmup(Dictionary<int, string> dcRedirects, bool forceTestDc)
    {
        foreach (var pair in dcRedirects)
        {
            int dc = pair.Key;
            string targetIp = pair.Value;
            if (string.IsNullOrEmpty(targetIp)) continue;

            foreach (bool isMedia in new[] { false, true })
            {
                var domains = GetWsDomains(dc, isMedia);
                ScheduleRefill((dc, isMedia), targetIp, domains);
            }
        }
    }

    public void Reset()
    {
        foreach (var bucket in _idle.Values)
        {
            while (bucket.TryDequeue(out var item))
            {
                _ = QuietCloseAsync(item.Ws);
            }
        }
        _idle.Clear();
        _refilling.Clear();
        FrontingUntil = DateTime.MinValue;
    }

    public static List<string> GetWsDomains(int dc, bool isMedia)
    {
        if (dc == 203) dc = 2;
        if (isMedia)
        {
            return new List<string> { $"kws{dc}-1.web.telegram.org", $"kws{dc}.web.telegram.org" };
        }
        return new List<string> { $"kws{dc}.web.telegram.org", $"kws{dc}-1.web.telegram.org" };
    }
}

public class CfWorkerPool
{
    private const double WS_POOL_MAX_AGE = 100.0;
    private readonly ConcurrentDictionary<(int Dc, string WorkerDomain), ConcurrentQueue<(RawWebSocket Ws, DateTime Created)>> _idle = new();
    private readonly ConcurrentDictionary<(int Dc, string WorkerDomain), bool> _refilling = new();
    private readonly Action<string> _logCallback;

    public int PoolSize { get; set; } = 4;

    public CfWorkerPool(Action<string> logCallback)
    {
        _logCallback = logCallback;
    }

    public async Task<RawWebSocket?> GetAsync(int dc, string workerDomain, string fallbackDst)
    {
        var key = (dc, workerDomain);
        var bucket = _idle.GetOrAdd(key, _ => new ConcurrentQueue<(RawWebSocket Ws, DateTime Created)>());

        while (bucket.TryDequeue(out var item))
        {
            double age = (DateTime.UtcNow - item.Created).TotalSeconds;
            if (age > WS_POOL_MAX_AGE || item.Ws.IsClosed)
            {
                _ = QuietCloseAsync(item.Ws);
                continue;
            }

            _logCallback($"[CfWorkerPool] Pool hit for DC{dc} via worker {workerDomain} (age={age:F1}s, left={bucket.Count})");
            ScheduleRefill(key, fallbackDst);
            return item.Ws;
        }

        ScheduleRefill(key, fallbackDst);
        return null;
    }

    private void ScheduleRefill((int Dc, string WorkerDomain) key, string fallbackDst)
    {
        if (_refilling.TryAdd(key, true))
        {
            Task.Run(async () =>
            {
                try
                {
                    await RefillAsync(key, fallbackDst);
                }
                finally
                {
                    _refilling.TryRemove(key, out _);
                }
            });
        }
    }

    private async Task RefillAsync((int Dc, string WorkerDomain) key, string fallbackDst)
    {
        var bucket = _idle.GetOrAdd(key, _ => new ConcurrentQueue<(RawWebSocket Ws, DateTime Created)>());
        int needed = PoolSize - bucket.Count;
        if (needed <= 0)
            return;

        var tasks = new List<Task<RawWebSocket?>>();
        for (int i = 0; i < needed; i++)
        {
            tasks.Add(ConnectOneAsync(key.WorkerDomain, fallbackDst, key.Dc));
        }

        var results = await Task.WhenAll(tasks);
        int added = 0;
        foreach (var ws in results)
        {
            if (ws != null)
            {
                bucket.Enqueue((ws, DateTime.UtcNow));
                added++;
            }
        }

        if (added > 0)
        {
            _logCallback($"[CfWorkerPool] Refilled DC{key.Dc} via {key.WorkerDomain}: {bucket.Count} ready.");
        }
    }

    private static async Task<RawWebSocket?> ConnectOneAsync(string workerDomain, string fallbackDst, int dc)
    {
        string path = $"/apiws?dst={Uri.EscapeDataString(fallbackDst)}&dc={dc}";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            return await RawWebSocket.ConnectAsync(workerDomain, workerDomain, path, null, cts.Token);
        }
        catch
        {
            return null;
        }
    }

    private static async Task QuietCloseAsync(RawWebSocket ws)
    {
        try
        {
            await ws.CloseAsync();
        }
        catch { }
    }

    public void Warmup(List<string> workerDomains, Dictionary<int, string> dcRedirects)
    {
        var cfFallbacks = Constants.DC_DEFAULT_IPS
            .Where(pair => !dcRedirects.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (cfFallbacks.Count == 0 || workerDomains.Count == 0) return;

        foreach (string workerDomain in workerDomains)
        {
            foreach (var pair in cfFallbacks)
            {
                ScheduleRefill((pair.Key, workerDomain), pair.Value);
            }
        }
    }

    public void Reset()
    {
        foreach (var bucket in _idle.Values)
        {
            while (bucket.TryDequeue(out var item))
            {
                _ = QuietCloseAsync(item.Ws);
            }
        }
        _idle.Clear();
        _refilling.Clear();
    }
}
