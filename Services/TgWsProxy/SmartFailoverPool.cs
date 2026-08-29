using System;
using System.Collections.Concurrent;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public enum ProxyStrategy
{
    DirectWs,
    Fronting
}

public class SmartFailoverPool
{
    private class StrategyHealth
    {
        public int Successes { get; set; }
        public int Failures { get; set; }
        public DateTime CooldownUntil { get; set; } = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<string, StrategyHealth> _health = new();
    private readonly Action<string> _logCallback;

    public static SmartFailoverPool Instance { get; } = new SmartFailoverPool(msg => { });

    public SmartFailoverPool(Action<string> logCallback)
    {
        _logCallback = logCallback;
    }

    public bool IsStrategyHealthy(int dc, ProxyStrategy strategy)
    {
        string key = $"{dc}_{strategy}";
        if (_health.TryGetValue(key, out var h))
        {
            if (DateTime.UtcNow < h.CooldownUntil)
                return false;
        }
        return true;
    }

    public void RecordSuccess(int dc, ProxyStrategy strategy)
    {
        string key = $"{dc}_{strategy}";
        var h = _health.GetOrAdd(key, _ => new StrategyHealth());
        lock (h)
        {
            h.Successes++;
            h.Failures = 0;
            h.CooldownUntil = DateTime.MinValue;
        }
    }

    public void RecordFailure(int dc, ProxyStrategy strategy, double cooldownSeconds = 120.0)
    {
        string key = $"{dc}_{strategy}";
        var h = _health.GetOrAdd(key, _ => new StrategyHealth());
        lock (h)
        {
            h.Failures++;
            if (h.Failures >= 2)
            {
                h.CooldownUntil = DateTime.UtcNow.AddSeconds(cooldownSeconds);
                _logCallback($"[SMART_FAILOVER] Канал {strategy} для DC{dc} временно отправлен на паузу ({cooldownSeconds}с) из-за ошибок.");
            }
        }
    }
}
