using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services;

public enum DiagnosticEventType
{
    Log,
    PresetStarted,
    PresetFinished,
    TargetTested,
    AllDone
}

public record DiagnosticProgressEvent(
    DiagnosticEventType Type,
    string Message,
    bool IsError = false,
    string? PresetName = null,
    PresetResult? Result = null);

public class DiagnosticOptions
{
    public string TestType { get; init; } = "standard";
    public List<string> Presets { get; init; } = new();
    public int TimeoutSeconds { get; init; } = 5;
    public int InitDelaySeconds { get; init; } = 3;
    public string GameFilterMode { get; init; } = "disabled";
}

public record PresetResult(
    string PresetName,
    int Ok,
    int Fail,
    int Unsup,
    int Blocked,
    long AvgPingMs,
    bool IsDpi = false);

public record TargetConfig(string Name, string? Url, string? PingHost);

// ── Diagnostic Engine (Flowseal Compatible Curl Process Runner) ───────────────

public static class DiagnosticEngine
{
    private const int MaxParallel = 8;

    public static async Task RunAsync(
        DiagnosticOptions options,
        IProgress<DiagnosticProgressEvent> progress,
        CancellationToken ct)
    {
        var root     = ZapretService.FindZapretRoot();
        var listsDir = ZapretService.FindListsDirectory();
        var curlPath = FindCurl(root);

        Log(progress, $"[ИНФО] Использование движка проверки: {curlPath}");

        // Guarantee user list files exist natively in C#
        PresetManager.EnsureUserListsExist();
        ZapretService.EnableTcpTimestamps();

        var ipsetFlagFile = Path.Combine(root, "ipset_switched.flag");
        bool ipsetSwitched = false;

        if (File.Exists(ipsetFlagFile))
        {
            Log(progress, "[ИНФО] Обнаружен флаг переключения ipset. Восстанавливаем...");
            RestoreIpset(listsDir);
            TryDelete(ipsetFlagFile);
        }

        var originalIpset = GetIpsetStatus(listsDir);
        Log(progress, $"[ИНФО] Текущий статус ipset: {originalIpset}");

        // Temporarily switch ipset to 'any' mode for testing (so winws desync applies to all IPs)
        if (originalIpset != "any")
        {
            Log(progress, "[ВНИМАНИЕ] Временно переключаем ipset в режим 'any'...");
            SwitchIpsetToAny(listsDir);
            File.WriteAllText(ipsetFlagFile, "", Encoding.UTF8);
            ipsetSwitched = true;
        }

        var targets = LoadTargets(root);
        Log(progress, $"[ИНФО] Загружено целей для тестирования: {targets.Count}");

        try
        {
            int configNum = 0;
            foreach (var preset in options.Presets)
            {
                ct.ThrowIfCancellationRequested();
                configNum++;

                Log(progress, "");
                Log(progress, "──────────────────────────────────────────────────────────────");
                Log(progress, $"  [{configNum}/{options.Presets.Count}] {preset}");
                Log(progress, "──────────────────────────────────────────────────────────────");

                progress.Report(new DiagnosticProgressEvent(
                    DiagnosticEventType.PresetStarted, $"[▶] Запуск проверки: {preset}", PresetName: preset));

                ZapretService.KillAllWinwsProcesses();
                await Task.Delay(200, ct);

                using var winws = StartWinwsForPreset(root, preset, progress);
                if (winws == null)
                {
                    Log(progress, $"[ПРОПУСК] Сбой запуска стратегии '{preset}'", isError: true);
                    continue;
                }

                int waitMs = Math.Max(3, options.InitDelaySeconds) * 1000;
                if (waitMs > 0)
                {
                    await Task.Delay(waitMs, ct);
                }

                if (winws.HasExited)
                {
                    Log(progress, $"[ОШИБКА] winws.exe завершился сразу (код {winws.ExitCode}). Пропуск стратегии.", isError: true);
                    continue;
                }

                Log(progress, "[▶] Тестирование сетевой доступности через curl...");

                PresetResult result;
                if (options.TestType == "dpi")
                {
                    result = await RunDpiTestsAsync(curlPath, options.TimeoutSeconds, preset, progress, ct);
                }
                else
                {
                    result = await RunStandardTestsAsync(curlPath, targets, options.TimeoutSeconds, preset, progress, ct);
                }

                Log(progress, $"  ➜ Итог '{preset}': OK={result.Ok} Fail={result.Fail} Unsup={result.Unsup} Blocked={result.Blocked} AvgPing={(result.AvgPingMs >= 0 ? $"{result.AvgPingMs} мс" : "н/д")}");

                progress.Report(new DiagnosticProgressEvent(
                    DiagnosticEventType.PresetFinished, "", PresetName: preset, Result: result));

                try { if (!winws.HasExited) winws.Kill(true); } catch { }
            }

            Log(progress, "");
            Log(progress, "==============================================================");
            Log(progress, "🎉 Все проверки успешно завершены!");
            Log(progress, "==============================================================");

            progress.Report(new DiagnosticProgressEvent(DiagnosticEventType.AllDone, "Все тесты завершены"));
        }
        finally
        {
            ZapretService.KillAllWinwsProcesses();
            if (ipsetSwitched)
            {
                Log(progress, "[ИНФО] Восстанавливаем исходный статус ipset...");
                RestoreIpset(listsDir);
                TryDelete(ipsetFlagFile);
            }
        }
    }

    // ── Standard tests ─────────────────────────────────────────────────────────

    private static async Task<PresetResult> RunStandardTestsAsync(
        string curlPath, List<TargetConfig> targets, int timeoutSec, string presetName,
        IProgress<DiagnosticProgressEvent> progress, CancellationToken ct)
    {
        int totalOk = 0, totalFail = 0, totalUnsup = 0;
        long totalPing = 0; int pingCount = 0;
        var lockObj = new object();
        var sem = new SemaphoreSlim(MaxParallel, MaxParallel);

        var tests = new[]
        {
            new { Label = "HTTP",   Args = new[] { "--http1.1" } },
            new { Label = "TLS1.2", Args = new[] { "--tlsv1.2", "--tls-max", "1.2" } },
            new { Label = "TLS1.3", Args = new[] { "--tlsv1.3", "--tls-max", "1.3" } }
        };

        var tasks = targets.Select(async target =>
        {
            ct.ThrowIfCancellationRequested();

            int ok = 0, fail = 0, unsup = 0;
            long pingMs = -1;
            var httpParts = new List<string>();

            if (target.Url != null)
            {
                foreach (var test in tests)
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        var argsList = new List<string> {
                            "-k", "--ssl-no-revoke",
                            "-I",
                            "-s", "-S",
                            "-m", timeoutSec.ToString(),
                            "--connect-timeout", timeoutSec.ToString(),
                            "-w", "%{http_code}",
                            "-o", "NUL"
                        };
                        argsList.AddRange(test.Args);
                        argsList.Add(target.Url);

                        var (outStr, errStr, exit) = await RunCurlAsync(curlPath, argsList.ToArray(), ct);
                        
                        bool dnsHijack = errStr.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase);

                        bool unsupported = (exit == 35) || 
                                           errStr.Contains("does not support", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("protocol not supported", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("unsupported protocol", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("TLS not supported", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("Unrecognized option", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("Unknown option", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("unsupported option", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("unsupported feature", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("schannel", StringComparison.OrdinalIgnoreCase);

                        bool isHttpOk = (exit == 0) || (outStr.Length == 3 && int.TryParse(outStr, out int httpStatusCode) && httpStatusCode >= 200 && httpStatusCode < 600);

                        if (dnsHijack)
                        {
                            fail++;
                            httpParts.Add($"{test.Label}:DNS_FAIL");
                        }
                        else if (unsupported)
                        {
                            unsup++;
                            httpParts.Add($"{test.Label}:UNSUP");
                        }
                        else if (isHttpOk)
                        {
                            ok++;
                            httpParts.Add($"{test.Label}:OK");
                        }
                        else
                        {
                            fail++;
                            httpParts.Add($"{test.Label}:ERROR");
                        }
                    }
                    finally { sem.Release(); }
                }
            }

            if (target.PingHost != null)
            {
                await sem.WaitAsync(ct);
                try   { pingMs = await MeasurePingAsync(target.PingHost, timeoutSec * 1000, ct); }
                finally { sem.Release(); }
            }

            string httpStr = target.Url != null ? string.Join("  ", httpParts) + "  | " : "";
            string pingStr = target.PingHost == null ? "" :
                             pingMs >= 0 ? $"Ping: {pingMs} мс" : "Ping: таймаут";
            progress.Report(new DiagnosticProgressEvent(DiagnosticEventType.TargetTested,
                $"  {target.Name.Replace(" ", "").PadRight(26)} {httpStr}{pingStr}"));

            lock (lockObj)
            {
                totalOk += ok; totalFail += fail; totalUnsup += unsup;
                if (pingMs >= 0) { totalPing += pingMs; pingCount++; }
            }
        }).ToList();

        await Task.WhenAll(tasks);
        long avgPing = pingCount > 0 ? totalPing / pingCount : -1;
        return new PresetResult(presetName, totalOk, totalFail, totalUnsup, 0, avgPing, IsDpi: false);
    }

    // ── DPI TCP 16-20KB tests ──────────────────────────────────────────────────

    private static async Task<PresetResult> RunDpiTestsAsync(
        string curlPath, int timeoutSec, string presetName,
        IProgress<DiagnosticProgressEvent> progress, CancellationToken ct)
    {
        var dpiHosts = new[]
        {
            "www.youtube.com", "gateway.discord.gg", "www.facebook.com",
            "www.instagram.com", "www.tiktok.com", "api.twitter.com"
        };
        int rangeBytes = 65536;
        var rangeSpec  = $"0-{rangeBytes - 1}";
        var payload    = new byte[rangeBytes];
        System.Security.Cryptography.RandomNumberGenerator.Fill(payload);
        var payloadFile = Path.GetTempFileName();
        File.WriteAllBytes(payloadFile, payload);

        int totalOk = 0, totalFail = 0, totalBlocked = 0, totalUnsup = 0;
        var lockObj = new object();
        var sem = new SemaphoreSlim(MaxParallel, MaxParallel);

        var tests = new[]
        {
            new { Label = "HTTP",   Args = new[] { "--http1.1" } },
            new { Label = "TLS1.2", Args = new[] { "--tlsv1.2", "--tls-max", "1.2" } },
            new { Label = "TLS1.3", Args = new[] { "--tlsv1.3", "--tls-max", "1.3" } }
        };

        try
        {
            var tasks = dpiHosts.Select(async host =>
            {
                ct.ThrowIfCancellationRequested();

                int ok = 0, fail = 0, blockedCount = 0, unsup = 0;
                var httpParts = new List<string>();

                foreach (var test in tests)
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        var argsList = new List<string> {
                            "-k", "--ssl-no-revoke",
                            "--range", rangeSpec,
                            "-m", timeoutSec.ToString(),
                            "--connect-timeout", timeoutSec.ToString(),
                            "-w", "%{http_code} %{size_upload} %{size_download} %{time_total}",
                            "-o", "NUL",
                            "-X", "POST",
                            "--data-binary", $"@{payloadFile}",
                            "-s"
                        };
                        argsList.AddRange(test.Args);
                        argsList.Add($"https://{host}");

                        var (outStr, errStr, exit) = await RunCurlAsync(curlPath, argsList.ToArray(), ct);
                        ParseDpiOutput(outStr, errStr, exit, out string code, out long upBytes, out long downBytes, out double time);

                        bool isBlocked = (upBytes > 0 && downBytes == 0 && time >= timeoutSec && exit != 0);
                        bool unsupported = (exit == 35) || 
                                           errStr.Contains("does not support", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("protocol not supported", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("unsupported protocol", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("TLS not supported", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("Unrecognized option", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("Unknown option", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("unsupported option", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("unsupported feature", StringComparison.OrdinalIgnoreCase) ||
                                           errStr.Contains("schannel", StringComparison.OrdinalIgnoreCase);

                        if (isBlocked)
                        {
                            blockedCount++;
                            httpParts.Add($"{test.Label}:BLOCKED");
                        }
                        else if (unsupported)
                        {
                            unsup++;
                            httpParts.Add($"{test.Label}:UNSUP");
                        }
                        else if (exit == 0 && code != "UNSUP" && code != "ERR")
                        {
                            ok++;
                            httpParts.Add($"{test.Label}:OK");
                        }
                        else
                        {
                            fail++;
                            httpParts.Add($"{test.Label}:FAIL");
                        }
                    }
                    finally { sem.Release(); }
                }

                string httpStr = string.Join("  ", httpParts);
                progress.Report(new DiagnosticProgressEvent(DiagnosticEventType.TargetTested,
                    $"  {host.PadRight(26)} {httpStr}"));

                lock (lockObj)
                {
                    totalOk += ok; totalFail += fail; totalBlocked += blockedCount; totalUnsup += unsup;
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }
        finally
        {
            TryDelete(payloadFile);
        }

        return new PresetResult(presetName, totalOk, totalFail, totalUnsup, totalBlocked, -1, IsDpi: true);
    }

    private static void ParseDpiOutput(string text, string errorText, int exit, out string code, out long upBytes, out long downBytes, out double time)
    {
        code = "ERR"; upBytes = 0; downBytes = 0; time = -1;
        var m = Regex.Match(text.Trim(), @"^(?<code>\d{3})\s+(?<up>\d+)\s+(?<down>\d+)\s+(?<time>[\d\.]+)$");
        if (m.Success)
        {
            code = m.Groups["code"].Value;
            long.TryParse(m.Groups["up"].Value, out upBytes);
            long.TryParse(m.Groups["down"].Value, out downBytes);
            double.TryParse(m.Groups["time"].Value, out time);
        }
        else if (exit == 35 || 
                 errorText.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                 errorText.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
        {
            code = "UNSUP";
        }
    }

    // ── Process Execution for curl with WorkingDirectory = zapret/bin ──────────

    private static async Task<(string Output, string Error, int ExitCode)> RunCurlAsync(
        string curlPath, string[] args, CancellationToken ct)
    {
        var workDir = Path.GetDirectoryName(curlPath);
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
        {
            workDir = ZapretService.FindZapretRoot();
        }

        var psi = new ProcessStartInfo
        {
            FileName               = curlPath,
            WorkingDirectory       = workDir,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = new Process { StartInfo = psi };
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();

        proc.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
            return (sbOut.ToString().Trim(), sbErr.ToString().Trim(), proc.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { }
            throw;
        }
    }

    // ── Ping ───────────────────────────────────────────────────────────────────

    private static async Task<long> MeasurePingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
        }
        catch
        {
            return -1;
        }
    }

    // ── Target Loader ──────────────────────────────────────────────────────────

    private static List<TargetConfig> LoadTargets(string root)
    {
        var targetsFile = Path.Combine(root, "utils", "targets.txt");
        var list = new List<TargetConfig>();

        if (File.Exists(targetsFile))
        {
            foreach (var line in File.ReadAllLines(targetsFile))
            {
                var m = Regex.Match(line, @"^\s*(\w[\w\s]*)\s*=\s*""(.+)""\s*$");
                if (m.Success)
                {
                    var name  = m.Groups[1].Value.Trim();
                    var val   = m.Groups[2].Value.Trim();
                    list.Add(ParseTargetValue(name, val));
                }
            }
        }

        if (list.Count == 0)
        {
            list.AddRange(new TargetConfig[]
            {
                new("Discord Main",           "https://discord.com",         "discord.com"),
                new("Discord Gateway",        "https://gateway.discord.gg",  "gateway.discord.gg"),
                new("Discord CDN",            "https://cdn.discordapp.com",  "cdn.discordapp.com"),
                new("Discord Updates",        "https://updates.discord.com", "updates.discord.com"),
                new("YouTube Web",            "https://www.youtube.com",     "www.youtube.com"),
                new("YouTube Short",          "https://youtu.be",            "youtu.be"),
                new("YouTube Image",          "https://i.ytimg.com",         "i.ytimg.com"),
                new("YouTube Video Redirect", "https://redirector.googlevideo.com", "redirector.googlevideo.com"),
                new("Google Main",            "https://www.google.com",      "www.google.com"),
                new("Google Gstatic",         "https://www.gstatic.com",     "www.gstatic.com"),
                new("Cloudflare Web",         "https://www.cloudflare.com",  "www.cloudflare.com"),
                new("Cloudflare CDN",         "https://cdnjs.cloudflare.com","cdnjs.cloudflare.com"),
                new("Cloudflare DNS 1.1.1.1", null,                          "1.1.1.1"),
                new("Cloudflare DNS 1.0.0.1", null,                          "1.0.0.1"),
                new("Google DNS 8.8.8.8",     null,                          "8.8.8.8"),
                new("Google DNS 8.8.4.4",     null,                          "8.8.8.8"),
                new("Quad9 DNS 9.9.9.9",      null,                          "9.9.9.9"),
            });
        }
        return list;
    }

    private static TargetConfig ParseTargetValue(string name, string val)
    {
        if (val.StartsWith("PING:", StringComparison.OrdinalIgnoreCase))
        {
            var host = val.Substring(5).Trim();
            return new TargetConfig(name, null, host);
        }
        var pingHost = Regex.Replace(val, @"^https?://", "");
        pingHost     = Regex.Replace(pingHost, @"/.*$", "");
        return new TargetConfig(name, val, pingHost);
    }

    // ── winws launch ───────────────────────────────────────────────────────────

    private static Process? StartWinwsForPreset(
        string root, string preset, IProgress<DiagnosticProgressEvent> progress)
    {
        PresetManager.EnsureUserListsExist();
        var winwsPath = Path.Combine(root, "bin", "winws.exe");
        if (!File.Exists(winwsPath))
        {
            Log(progress, $"[ОШИБКА] winws.exe не найден: {winwsPath}", isError: true);
            return null;
        }
        var args = ZapretService.ParseArgumentsFromBatch(preset, "disabled");
        if (string.IsNullOrWhiteSpace(args))
        {
            Log(progress, $"[ОШИБКА] Не удалось разобрать аргументы из '{preset}'", isError: true);
            return null;
        }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = winwsPath,
                Arguments = args,
                WorkingDirectory = Path.Combine(root, "bin"),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = new Process { StartInfo = psi };
            proc.Start();
            return proc;
        }
        catch (Exception ex)
        {
            Log(progress, $"[ОШИБКА] Не удалось запустить winws.exe: {ex.Message}", isError: true);
            return null;
        }
    }

    // ── ipset ──────────────────────────────────────────────────────────────────

    private static string GetIpsetStatus(string listsDir)
    {
        var listFile = Path.Combine(listsDir, "ipset-all.txt");
        if (!File.Exists(listFile)) return "none";
        var lines = File.ReadAllLines(listFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length == 0) return "any";
        return lines.Any(l => l.Contains("203.0.113.113/32")) ? "none" : "loaded";
    }

    private static void SwitchIpsetToAny(string listsDir)
    {
        var listFile   = Path.Combine(listsDir, "ipset-all.txt");
        var backupFile = Path.Combine(listsDir, "ipset-all.test-backup.txt");
        if (File.Exists(listFile)) File.Copy(listFile, backupFile, overwrite: true);
        else File.WriteAllText(backupFile, "");
        File.WriteAllText(listFile, "");
    }

    private static void RestoreIpset(string listsDir)
    {
        var listFile   = Path.Combine(listsDir, "ipset-all.txt");
        var backupFile = Path.Combine(listsDir, "ipset-all.test-backup.txt");
        if (File.Exists(backupFile)) File.Move(backupFile, listFile, overwrite: true);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static string FindCurl(string root)
    {
        var bundled = Path.Combine(root, "bin", "curl.exe");
        return File.Exists(bundled) ? bundled : "curl.exe";
    }

    private static void Log(IProgress<DiagnosticProgressEvent> p, string msg, bool isError = false)
        => p.Report(new DiagnosticProgressEvent(DiagnosticEventType.Log, msg, isError));

    private static void TryDelete(string path)
    { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
