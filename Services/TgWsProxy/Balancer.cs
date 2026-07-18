using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class Balancer
{
    private static readonly string[] CFPROXY_ENC = {
        "virkgj.com", "vmmzovy.com", "mkuosckvso.com", "zaewayzmplad.com", "twdmbzcm.com",
        "awzwsldi.com", "clngqrflngqin.com", "tjacxbqtj.com", "bxaxtxmrw.com", "dmohrsgmohcrwb.com",
        "vwbmtmoi.com", "khgrre.com", "ulihssf.com", "tmhqsdqmfpmk.com", "xwuwoqbm.com",
        "orgcnunpj.com", "zhkuldz.com", "zypoljnslxa.com", "efabnxaowuzs.com", "zaftuzsftqdq.com"
    };

    private const string S = ".co.uk";
    private const string CFPROXY_DOMAINS_URL = "https://raw.githubusercontent.com/Flowseal/tg-ws-proxy/main/.github/cfproxy-domains.txt";

    private readonly object _lock = new();
    private List<string> _domains = new();
    private readonly Dictionary<int, string> _dcToDomain = new();
    private CancellationTokenSource? _cts;

    public static Balancer Instance { get; } = new Balancer();

    private Balancer()
    {
        var defaults = CFPROXY_ENC.Select(DecodeDomain).ToList();
        UpdateDomainsList(defaults);
    }

    public static string DecodeDomain(string s)
    {
        if (!s.EndsWith(".com", StringComparison.OrdinalIgnoreCase))
            return s;

        string p = s.Substring(0, s.Length - 4);
        int n = 0;
        foreach (char c in p)
        {
            if (char.IsLetter(c)) n++;
        }

        var sb = new StringBuilder();
        foreach (char c in p)
        {
            if (char.IsLetter(c))
            {
                int baseChar = c > '`' ? 97 : 65;
                int val = c - baseChar - n;
                while (val < 0) val += 26;
                sb.Append((char)(val % 26 + baseChar));
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append(S);
        return sb.ToString();
    }

    public void UpdateDomainsList(List<string> domains)
    {
        lock (_lock)
        {
            if (_domains.SequenceEqual(domains))
                return;

            _domains = domains.ToList();

            var rand = Random.Shared;
            int[] dcs = { 1, 2, 3, 4, 5, 203 };
            foreach (int dc in dcs)
            {
                if (_domains.Count > 0)
                {
                    _dcToDomain[dc] = _domains[rand.Next(_domains.Count)];
                }
            }
        }
    }

    public bool UpdateDomainForDc(int dcId, string domain)
    {
        lock (_lock)
        {
            if (_dcToDomain.TryGetValue(dcId, out string? current) && current == domain)
                return false;

            _dcToDomain[dcId] = domain;
            return true;
        }
    }

    public List<string> GetDomainsForDc(int dcId)
    {
        var result = new List<string>();
        lock (_lock)
        {
            _dcToDomain.TryGetValue(dcId, out string? current);
            if (current != null)
            {
                result.Add(current);
            }

            var rest = _domains.Where(d => d != current).ToList();
            var rand = Random.Shared;
            for (int i = rest.Count - 1; i > 0; i--)
            {
                int k = rand.Next(i + 1);
                string temp = rest[i];
                rest[i] = rest[k];
                rest[k] = temp;
            }
            result.AddRange(rest);
        }
        return result;
    }

    public void StartRefresh(Action<string> logCallback)
    {
        StopRefresh();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("tg-ws-proxy");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    string randQuery = Guid.NewGuid().ToString("N").Substring(0, 7);
                    string url = $"{CFPROXY_DOMAINS_URL}?{randQuery}";
                    
                    var response = await client.GetStringAsync(url, token);
                    var lines = response.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    
                    var fetched = new List<string>();
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                            continue;
                        
                        string decoded = DecodeDomain(trimmed);
                        if (IsValidDomain(decoded))
                        {
                            fetched.Add(decoded.ToLowerInvariant());
                        }
                    }

                    if (fetched.Count >= 3)
                    {
                        var uniqueFetched = fetched.Distinct().ToList();
                        UpdateDomainsList(uniqueFetched);
                        logCallback($"[Balancer] CF proxy domain pool updated from GitHub ({uniqueFetched.Count} domains).");
                    }
                    else
                    {
                        logCallback($"[Balancer Warning] Low quality domain list received from GitHub. Keeping current pool.");
                    }
                }
                catch (Exception ex)
                {
                    logCallback($"[Balancer Error] Failed to refresh CF domains: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void StopRefresh()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253) return false;
        if (domain.StartsWith(".") || domain.EndsWith(".")) return false;
        
        string[] labels = domain.Split('.');
        if (labels.Length < 2) return false;

        foreach (var label in labels)
        {
            if (string.IsNullOrEmpty(label) || label.Length > 63) return false;
            if (label.StartsWith("-") || label.EndsWith("-")) return false;
            if (!label.All(c => char.IsLetterOrDigit(c) || c == '-')) return false;
        }

        string tld = labels[^1];
        if (tld.Length < 2 || !tld.Any(char.IsLetter)) return false;

        return true;
    }
}
