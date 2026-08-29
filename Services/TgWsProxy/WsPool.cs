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
    private readonly ConcurrentDictionary<(int Dc, bool IsMedia), DateTime> _failCooldown = new();
    private readonly Action<string> _logCallback;
    private CancellationTokenSource _poolCts = new();

    public DateTime FrontingUntil { get; set; } = DateTime.MinValue;
    public int PoolSize { get; set; } = 4;

    public WsPool(Action<string> logCallback)
    {
        _logCallback = logCallback;
    }

    public async Task<RawWebSocket?> GetAsync(int dc, bool isMedia, string targetIp, List<string> domains, CancellationToken token = default)
    {
        if (_poolCts.IsCancellationRequested) return null;

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

            if (!_poolCts.IsCancellationRequested)
            {
                _logCallback($"[WsPool] Pool hit for DC{dc}{(isMedia ? "m" : "")} (age={age:F1}s, left={bucket.Count})");
            }
            ScheduleRefill(key, targetIp, domains);
            return item.Ws;
        }

        ScheduleRefill(key, targetIp, domains);
        return null;
    }

    private void ScheduleRefill((int Dc, bool IsMedia) key, string targetIp, List<string> domains)
    {
        if (_poolCts.IsCancellationRequested) return;

        if (_failCooldown.TryGetValue(key, out var cooldownUntil) && DateTime.UtcNow < cooldownUntil)
        {
            return;
        }

        if (_refilling.TryAdd(key, true))
        {
            var token = _poolCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    if (!token.IsCancellationRequested)
                    {
                        await RefillAsync(key, targetIp, domains, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
                finally
                {
                    _refilling.TryRemove(key, out _);
                }
            }, token);
        }
    }

    private async Task RefillAsync((int Dc, bool IsMedia) key, string targetIp, List<string> domains, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        var bucket = _idle.GetOrAdd(key, _ => new ConcurrentQueue<(RawWebSocket Ws, DateTime Created)>());
        int targetPool = key.IsMedia ? Math.Max(PoolSize, 6) : PoolSize;
        int needed = targetPool - bucket.Count;
        if (needed <= 0)
            return;

        var tasks = new List<Task<RawWebSocket?>>();
        bool isFronting = DateTime.UtcNow < FrontingUntil;
        for (int i = 0; i < needed; i++)
        {
            tasks.Add(ConnectOneAsync(targetIp, domains, isFronting, token));
        }

        var results = await Task.WhenAll(tasks);
        if (token.IsCancellationRequested)
        {
            foreach (var ws in results)
            {
                if (ws != null) _ = QuietCloseAsync(ws);
            }
            return;
        }

        int added = 0;
        foreach (var ws in results)
        {
            if (ws != null)
            {
                bucket.Enqueue((ws, DateTime.UtcNow));
                added++;
            }
        }

        if (added > 0 && !token.IsCancellationRequested)
        {
            _failCooldown.TryRemove(key, out _);
            _logCallback($"[WsPool] Refilled DC{key.Dc}{(key.IsMedia ? "m" : "")}: {bucket.Count} ready.");
        }
        else if (needed > 0 && !token.IsCancellationRequested)
        {
            _failCooldown[key] = DateTime.UtcNow.AddSeconds(20);
        }
    }

    private async Task<RawWebSocket?> ConnectOneAsync(string targetIp, List<string> domains, bool isFronting, CancellationToken token)
    {
        foreach (var domain in domains)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                string? sni = isFronting ? "sprinthost.ru" : domain;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cts.Token);
                return await RawWebSocket.ConnectAsync(targetIp, domain, "/apiws", sni, linked.Token);
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

    public void Warmup(Dictionary<int, string> dcRedirects, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        foreach (var pair in dcRedirects)
        {
            if (token.IsCancellationRequested) break;

            int dc = pair.Key;
            string configuredIp = pair.Value;
            string targetIp = IpBenchmarkPool.Instance.GetBestTargetIp(dc, configuredIp, true);
            if (string.IsNullOrEmpty(targetIp)) continue;

            foreach (bool isMedia in new[] { false, true })
            {
                if (token.IsCancellationRequested) break;
                var domains = GetWsDomains(dc, isMedia);
                ScheduleRefill((dc, isMedia), targetIp, domains);
            }
        }
    }

    public void Reset()
    {
        try
        {
            _poolCts.Cancel();
            _poolCts.Dispose();
        }
        catch { }
        _poolCts = new CancellationTokenSource();

        foreach (var bucket in _idle.Values)
        {
            while (bucket.TryDequeue(out var item))
            {
                _ = QuietCloseAsync(item.Ws);
            }
        }
        _idle.Clear();
        _refilling.Clear();
        _failCooldown.Clear();
        FrontingUntil = DateTime.MinValue;
    }

    public static List<string> GetWsDomains(int dc, bool isMedia)
    {
        if (dc == 203) dc = 2;
        string dcNamed = dc switch
        {
            1 => "aurora.web.telegram.org",
            2 => "venus.web.telegram.org",
            3 => "vesta.web.telegram.org",
            4 => "venus.web.telegram.org",
            5 => "pluto.web.telegram.org",
            _ => "web.telegram.org"
        };

        if (isMedia)
        {
            return new List<string> { $"kws{dc}-1.web.telegram.org", $"kws{dc}.web.telegram.org", dcNamed };
        }
        return new List<string> { $"kws{dc}.web.telegram.org", $"kws{dc}-1.web.telegram.org", dcNamed };
    }
}
