using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services;

public class LatencyResult
{
    public string TargetName { get; set; } = "";
    public string Url { get; set; } = "";
    public long LatencyMs { get; set; } = -1;
    public bool IsBlocked { get; set; } = false;
    public bool IsSuccess => LatencyMs >= 0 && !IsBlocked;

    public string FormattedText => IsBlocked ? "Заблокирован" : (IsSuccess ? $"{LatencyMs} мс" : "Недоступен");
}

public static class NetworkLatencyService
{
    private static readonly HttpClient _httpClient;

    static NetworkLatencyService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseProxy = false, // Direct check to test DPI behavior
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0");
    }

    /// <summary>
    /// Performs a real TLS SNI & HTTP probe to check DPI blocking and measure latency.
    /// In Russia, DPI does not block raw TCP SYN, but resets TLS SNI Client Hello.
    /// </summary>
    public static async Task<LatencyResult> MeasureHttpSniLatencyAsync(string targetName, string url, int timeoutMs = 2500)
    {
        var result = new LatencyResult
        {
            TargetName = targetName,
            Url = url,
            LatencyMs = -1,
            IsBlocked = false
        };

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();

            if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
            {
                result.LatencyMs = sw.ElapsedMilliseconds;
                result.IsBlocked = false;
            }
            else
            {
                result.LatencyMs = -1;
                result.IsBlocked = true;
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.LatencyMs = -1;
            result.IsBlocked = true; // Timeout / dropped packets by DPI
        }
        catch (HttpRequestException)
        {
            sw.Stop();
            result.LatencyMs = -1;
            result.IsBlocked = true; // Connection reset by DPI (TCP RST / SNI drop)
        }
        catch (Exception)
        {
            sw.Stop();
            result.LatencyMs = -1;
            result.IsBlocked = true;
        }

        return result;
    }

    /// <summary>
    /// Measures ICMP packet loss percentage (0-100%) to target host (e.g. 1.1.1.1)
    /// </summary>
    public static async Task<int> MeasurePacketLossAsync(string host = "1.1.1.1", int attempts = 3, int timeoutMs = 800)
    {
        int lost = 0;
        using var pinger = new Ping();

        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var reply = await pinger.SendPingAsync(host, timeoutMs);
                if (reply.Status != IPStatus.Success)
                {
                    lost++;
                }
            }
            catch
            {
                lost++;
            }
        }

        return (int)Math.Round((double)lost / attempts * 100.0);
    }

    /// <summary>
    /// Measures Telegram DC response through local SOCKS proxy port if running.
    /// </summary>
    public static async Task<LatencyResult> MeasureTgProxyDcLatencyAsync(string dcName, int localProxyPort, int timeoutMs = 2000)
    {
        var result = new LatencyResult
        {
            TargetName = dcName,
            Url = $"127.0.0.1:{localProxyPort}",
            LatencyMs = -1,
            IsBlocked = !TgWsProxyService.IsRunning
        };

        if (!TgWsProxyService.IsRunning)
        {
            return result;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync("127.0.0.1", localProxyPort, cts.Token);
            sw.Stop();

            result.LatencyMs = sw.ElapsedMilliseconds;
            result.IsBlocked = false;
        }
        catch
        {
            sw.Stop();
            result.LatencyMs = -1;
            result.IsBlocked = true;
        }

        return result;
    }
}
