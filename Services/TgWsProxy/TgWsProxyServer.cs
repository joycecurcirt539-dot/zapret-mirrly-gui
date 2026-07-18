using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class TgWsProxyServer
{
    private readonly int _port;
    private readonly string _host;
    private readonly byte[] _secretBytes;
    private readonly Dictionary<int, string> _dcRedirects;
    private readonly bool _cfProxyEnabled;
    private readonly List<string> _cfProxyWorkerDomains;
    private readonly string _fakeTlsDomain;
    private readonly bool _forceTestDc;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly Action<string> _logCallback;

    private readonly WsPool _wsPool;
    private readonly CfWorkerPool _cfWorkerPool;

    private readonly HashSet<string> _wsBlacklist = new();
    private readonly ConcurrentDictionary<string, DateTime> _dcFailUntil = new();
    private readonly ConcurrentDictionary<string, DateTime> _ipFailUntil = new();

    private const double IP_FAIL_COOLDOWN = 3600.0;
    private const double DC_FAIL_COOLDOWN = 60.0;
    private const double WS_FAIL_TIMEOUT = 2.0;
    private const double FRONTING_COOLDOWN = 1800.0;

    public TgWsProxyServer(
        string host,
        int port,
        string secretHex,
        Dictionary<int, string> dcRedirects,
        bool cfProxyEnabled,
        List<string> cfProxyWorkerDomains,
        string fakeTlsDomain,
        bool forceTestDc,
        Action<string> logCallback)
    {
        _host = host;
        _port = port;
        _secretBytes = Convert.FromHexString(secretHex);
        _dcRedirects = dcRedirects;
        _cfProxyEnabled = cfProxyEnabled;
        _cfProxyWorkerDomains = cfProxyWorkerDomains ?? new List<string>();
        _fakeTlsDomain = fakeTlsDomain?.Trim() ?? "";
        _forceTestDc = forceTestDc;
        _logCallback = logCallback;

        _wsPool = new WsPool(logCallback);
        _cfWorkerPool = new CfWorkerPool(logCallback);
    }

    public int PoolSize
    {
        get => _wsPool.PoolSize;
        set
        {
            _wsPool.PoolSize = value;
            _cfWorkerPool.PoolSize = value;
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _wsPool.Reset();
        _cfWorkerPool.Reset();
        _wsBlacklist.Clear();
        _dcFailUntil.Clear();
        _ipFailUntil.Clear();

        if (_cfProxyEnabled)
        {
            Balancer.Instance.StartRefresh(_logCallback);
        }

        // Start listener
        try
        {
            var ipAddress = IPAddress.Parse(_host);
            _listener = new TcpListener(ipAddress, _port);
            _listener.Start();
        }
        catch (Exception ex)
        {
            _logCallback($"[TG_SERVER ERROR] Failed to start TCP Listener on {_host}:{_port}: {ex.Message}");
            throw;
        }

        _logCallback("============================================================");
        _logCallback("  Telegram MTProto WS Bridge Proxy (In-Process C#)");
        _logCallback($"  Listening on   {_host}:{_port}");
        _logCallback($"  Secret:        {Convert.ToHexString(_secretBytes).ToLowerInvariant()}");
        if (!string.IsNullOrEmpty(_fakeTlsDomain))
        {
            _logCallback($"  Fake TLS:      {_fakeTlsDomain}");
        }
        _logCallback("  Target DC IPs:");
        foreach (var dc in _dcRedirects.Keys.OrderBy(k => k))
        {
            _logCallback($"    DC{dc}: {_dcRedirects[dc]}");
        }
        if (_cfProxyEnabled)
        {
            _logCallback("  CF proxy:      enabled");
        }
        if (_cfProxyWorkerDomains.Count > 0)
        {
            _logCallback($"  CF worker:     enabled ({string.Join(", ", _cfProxyWorkerDomains)})");
        }
        _logCallback("============================================================");

        // Warm up pools
        _wsPool.Warmup(_dcRedirects, _forceTestDc);
        _cfWorkerPool.Warmup(_cfProxyWorkerDomains, _dcRedirects);

        // Accept client connections loop
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _logCallback($"[TG_SERVER ERROR] Accept connection failed: {ex.Message}");
                    }
                }
            }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();

        if (_cfProxyEnabled)
        {
            Balancer.Instance.StopRefresh();
        }

        _wsPool.Reset();
        _cfWorkerPool.Reset();

        _logCallback("[TG_SERVER] Server stopped.");
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        string clientLabel = client.Client.RemoteEndPoint?.ToString() ?? "?";
        _logCallback($"[{clientLabel}] Client connected.");

        client.NoDelay = true;
        client.ReceiveBufferSize = 256 * 1024;
        client.SendBufferSize = 256 * 1024;

        Stream clientStream = client.GetStream();
        CryptoContext? cryptoCtx = null;
        MsgSplitter? splitter = null;

        try
        {
            byte[] handshake = new byte[Constants.HANDSHAKE_LEN];

            if (!string.IsNullOrEmpty(_fakeTlsDomain))
            {
                // Peek/read 5 bytes header
                byte[] header = new byte[5];
                int headerRead = 0;
                while (headerRead < 5)
                {
                    int r = await clientStream.ReadAsync(header.AsMemory(headerRead, 5 - headerRead), token);
                    if (r == 0) return;
                    headerRead += r;
                }

                if (header[0] == 0x16) // TLS Handshake
                {
                    ushort recordLen = (ushort)((header[3] << 8) | header[4]);
                    byte[] clientHello = new byte[5 + recordLen];
                    Array.Copy(header, 0, clientHello, 0, 5);

                    int bodyRead = 0;
                    while (bodyRead < recordLen)
                    {
                        int r = await clientStream.ReadAsync(clientHello.AsMemory(5 + bodyRead, recordLen - bodyRead), token);
                        if (r == 0) return;
                        bodyRead += r;
                    }

                    var tlsResult = FakeTls.VerifyClientHello(clientHello, _secretBytes);
                    if (tlsResult == null)
                    {
                        _logCallback($"[{clientLabel}] Fake TLS verification failed -> masking proxy to {_fakeTlsDomain}:443");
                        await ProxyToMaskingDomainAsync(clientStream, clientHello, _fakeTlsDomain, clientLabel, token);
                        return;
                    }

                    _logCallback($"[{clientLabel}] Fake TLS verification successful (TS: {tlsResult.Value.Timestamp}).");
                    
                    byte[] serverHello = FakeTls.BuildServerHello(_secretBytes, tlsResult.Value.ClientRandom, tlsResult.Value.SessionId);
                    await clientStream.WriteAsync(serverHello.AsMemory(), token);
                    await clientStream.FlushAsync(token);

                    clientStream = new FakeTlsStream(clientStream);
                }
                else
                {
                    // Non-TLS first byte, redirect client to HTTPS redirect page
                    _logCallback($"[{clientLabel}] Non-TLS first byte (0x{header[0]:X2}) under Fake TLS context -> Redirecting client to {_fakeTlsDomain}");
                    string redirect = $"HTTP/1.1 301 Moved Permanently\r\n" +
                                     $"Location: https://{_fakeTlsDomain}/\r\n" +
                                     $"Content-Length: 0\r\n" +
                                     $"Connection: close\r\n\r\n";
                    byte[] redirectBytes = Encoding.ASCII.GetBytes(redirect);
                    await clientStream.WriteAsync(redirectBytes.AsMemory(), token);
                    await clientStream.FlushAsync(token);
                    return;
                }
            }

            // Read the MTProto 64-byte obfuscated handshake
            int handshakeRead = 0;
            while (handshakeRead < Constants.HANDSHAKE_LEN)
            {
                int r = await clientStream.ReadAsync(handshake.AsMemory(handshakeRead, Constants.HANDSHAKE_LEN - handshakeRead), token);
                if (r == 0) return;
                handshakeRead += r;
            }

            var hsResult = TryHandshake(handshake, _secretBytes);
            if (hsResult == null)
            {
                _logCallback($"[{clientLabel}] Bad MTProto handshake (invalid secret/protocol).");
                return;
            }

            int dc = hsResult.Value.DcId;
            bool isMedia = hsResult.Value.IsMedia;
            byte[] protoTag = hsResult.Value.ProtoTag;
            byte[] clientDecPrekeyIv = hsResult.Value.DecPrekeyIv;

            int dcIdx = isMedia ? -dc : dc;
            uint protoInt = BitConverter.ToUInt32(protoTag, 0);

            _logCallback($"[{clientLabel}] Handshake OK: DC{dc}{(isMedia ? " media" : "")} proto=0x{protoInt:X8}");

            byte[] relayInit = GenerateRelayInit(protoTag, dcIdx);
            cryptoCtx = BuildCryptoContext(clientDecPrekeyIv, _secretBytes, relayInit);

            string dcKey = $"{dc}{(isMedia ? "m" : "")}";
            string targetIp = _dcRedirects.TryGetValue(dc, out string? ip) ? ip : "";
            bool isAnyCfFallback = _cfProxyEnabled || _cfProxyWorkerDomains.Count > 0;

            // Check if WS is blacklisted or destination IP is in cooldown
            bool wsBlacklisted = _wsBlacklist.Contains(dcKey);
            bool ipInCooldown = _ipFailUntil.TryGetValue(targetIp, out DateTime cooldownTime) && DateTime.UtcNow < cooldownTime;

            if (string.IsNullOrEmpty(targetIp) || wsBlacklisted || (ipInCooldown && isAnyCfFallback))
            {
                if (string.IsNullOrEmpty(targetIp))
                {
                    _logCallback($"[{clientLabel}] DC{dc} not in config -> fallback.");
                }
                else if (wsBlacklisted)
                {
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WebSocket is blacklisted -> fallback.");
                }
                else
                {
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} connect to {targetIp} is in cooldown -> fallback.");
                }

                try { splitter = new MsgSplitter(relayInit, protoInt); } catch {}
                bool fbOk = await DoFallbackAsync(clientStream, relayInit, clientLabel, dc, isMedia, cryptoCtx, splitter, token);
                if (!fbOk)
                {
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} no fallback connection available.");
                }
                return;
            }

            double wsTimeout = (_dcFailUntil.TryGetValue(dcKey, out DateTime failTime) && DateTime.UtcNow < failTime) ? WS_FAIL_TIMEOUT : 5.0;
            bool frontingActive = DateTime.UtcNow < _wsPool.FrontingUntil;

            var domains = WsPool.GetWsDomains(dc, isMedia);
            RawWebSocket? ws = null;
            bool wsFailedRedirect = false;
            bool wsTimedOut = false;
            bool allRedirects = true;

            // Try Pool Hit
            ws = await _wsPool.GetAsync(dc, isMedia, targetIp, domains);
            if (ws != null)
            {
                _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> pool hit via {targetIp}");
            }
            else if (frontingActive)
            {
                _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> fronting / Host {domains[0]}");
                try
                {
                    using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0));
                    ws = await RawWebSocket.ConnectAsync(targetIp, domains[0], "/apiws", "sprinthost.ru", wsCts.Token);
                }
                catch (Exception ex)
                {
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} fronting failed: {ex.Message}");
                }
                if (ws != null)
                {
                    _wsPool.FrontingUntil = DateTime.UtcNow.AddSeconds(FRONTING_COOLDOWN);
                }
                else
                {
                    _wsPool.FrontingUntil = DateTime.MinValue;
                }
            }
            else
            {
                // Connect manually to WebSocket domains
                foreach (string domain in domains)
                {
                    string url = $"wss://{domain}/apiws";
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> {url} via {targetIp}");

                    try
                    {
                        using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(wsTimeout));
                        ws = await RawWebSocket.ConnectAsync(targetIp, domain, "/apiws", null, wsCts.Token);
                        allRedirects = false;
                        break;
                    }
                    catch (WsHandshakeException ex)
                    {
                        if (ex.IsRedirect)
                        {
                            wsFailedRedirect = true;
                            _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} got redirect {ex.StatusCode} from {domain}");
                        }
                        else
                        {
                            allRedirects = false;
                            _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WS handshake failed: {ex.Message}");
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        wsTimedOut = true;
                        _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WS connect timed out via {domain}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        allRedirects = false;
                        _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WS connect failed: {ex.Message}");
                    }
                }
            }

            // Fronting Fallback if timed out
            if (ws == null && wsTimedOut && !frontingActive)
            {
                _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> fronting fallback (Host {domains[0]})");
                try
                {
                    using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0));
                    ws = await RawWebSocket.ConnectAsync(targetIp, domains[0], "/apiws", "sprinthost.ru", wsCts.Token);
                }
                catch (Exception ex)
                {
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} fronting fallback failed: {ex.Message}");
                }
                if (ws != null)
                {
                    _wsPool.FrontingUntil = DateTime.UtcNow.AddSeconds(FRONTING_COOLDOWN);
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} fronting OK for {FRONTING_COOLDOWN}s");
                }
            }

            // WebSocket failed -> Fallback
            if (ws == null)
            {
                if (wsTimedOut)
                {
                    _ipFailUntil[targetIp] = DateTime.UtcNow.AddSeconds(IP_FAIL_COOLDOWN);
                }

                if (wsFailedRedirect && allRedirects)
                {
                    _wsBlacklist.Add(dcKey);
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} blacklisted for WS (all redirects).");
                }
                else
                {
                    _dcFailUntil[dcKey] = DateTime.UtcNow.AddSeconds(DC_FAIL_COOLDOWN);
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WS cooldown for {DC_FAIL_COOLDOWN}s");
                }

                try { splitter = new MsgSplitter(relayInit, protoInt); } catch {}
                bool fbOk = await DoFallbackAsync(clientStream, relayInit, clientLabel, dc, isMedia, cryptoCtx, splitter, token);
                if (!fbOk)
                {
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} no fallback connection available.");
                }
                return;
            }

            // Remove failures on success
            _dcFailUntil.TryRemove(dcKey, out _);
            _ipFailUntil.TryRemove(targetIp, out _);

            try { splitter = new MsgSplitter(relayInit, protoInt); } catch {}

            // Send handshake
            await ws.SendAsync(relayInit, token);

            // Bridge session
            await BridgeWsReencryptAsync(clientStream, ws, cryptoCtx, splitter, clientLabel, dcKey, token);
        }
        catch (Exception ex)
        {
            _logCallback($"[{clientLabel}] unexpected error: {ex.Message}");
        }
        finally
        {
            cryptoCtx?.Dispose();
            splitter?.Dispose();
            clientStream.Dispose();
            client.Dispose();
            _logCallback($"[{clientLabel}] Connection closed.");
        }
    }

    private async Task<bool> DoFallbackAsync(
        Stream clientStream, byte[] relayInit, string clientLabel,
        int dc, bool isMedia, CryptoContext ctx, MsgSplitter? splitter, CancellationToken token)
    {
        string fallbackDst = Constants.DC_DEFAULT_IPS.TryGetValue(dc, out string? ip) ? ip : "";
        bool useCf = _cfProxyEnabled;

        var methods = new List<string>();
        if (_cfProxyWorkerDomains.Count > 0 && !string.IsNullOrEmpty(fallbackDst))
            methods.Add("cf_worker");
        if (useCf)
            methods.Add("cf");
        if (!string.IsNullOrEmpty(fallbackDst))
            methods.Add("tcp");

        foreach (string method in methods)
        {
            if (method == "cf_worker")
            {
                bool ok = await CfProxyWorkerFallbackAsync(clientStream, relayInit, clientLabel, dc, isMedia, fallbackDst, ctx, splitter, token);
                if (ok) return true;
            }
            else if (method == "cf")
            {
                bool ok = await CfProxyFallbackAsync(clientStream, relayInit, clientLabel, dc, isMedia, ctx, splitter, token);
                if (ok) return true;
            }
            else if (method == "tcp")
            {
                _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> TCP fallback to {fallbackDst}:443");
                bool ok = await TcpFallbackAsync(clientStream, fallbackDst, 443, relayInit, clientLabel, ctx, token);
                if (ok) return true;
            }
        }

        return false;
    }

    private async Task<bool> CfProxyWorkerFallbackAsync(
        Stream clientStream, byte[] relayInit, string clientLabel,
        int dc, bool isMedia, string fallbackDst, CryptoContext ctx, MsgSplitter? splitter, CancellationToken token)
    {
        var shuffledWorkers = _cfProxyWorkerDomains.ToList();
        var rand = Random.Shared;
        for (int i = shuffledWorkers.Count - 1; i > 0; i--)
        {
            int k = rand.Next(i + 1);
            string temp = shuffledWorkers[i];
            shuffledWorkers[i] = shuffledWorkers[k];
            shuffledWorkers[k] = temp;
        }

        foreach (string workerDomain in shuffledWorkers)
        {
            RawWebSocket? ws = await _cfWorkerPool.GetAsync(dc, workerDomain, fallbackDst);
            if (ws != null)
            {
                _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> pool hit via CF Worker {workerDomain}");
            }
            else
            {
                _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> trying CF Worker {workerDomain} for {fallbackDst}");
                string path = $"/apiws?dst={Uri.EscapeDataString(fallbackDst)}&dc={dc}";
                try
                {
                    using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
                    ws = await RawWebSocket.ConnectAsync(workerDomain, workerDomain, path, null, wsCts.Token);
                }
                catch (Exception ex)
                {
                    _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} CF Worker {workerDomain} failed: {ex.Message}");
                    continue;
                }
            }

            await ws.SendAsync(relayInit, token);
            await BridgeWsReencryptAsync(clientStream, ws, ctx, splitter, clientLabel, $"DC{dc}{(isMedia ? "m" : "")}", token);
            return true;
        }

        return false;
    }

    private async Task<bool> CfProxyFallbackAsync(
        Stream clientStream, byte[] relayInit, string clientLabel,
        int dc, bool isMedia, CryptoContext ctx, MsgSplitter? splitter, CancellationToken token)
    {
        _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> trying CF proxy");

        foreach (string baseDomain in Balancer.Instance.GetDomainsForDc(dc))
        {
            string domain = $"kws{dc}.{baseDomain}";
            try
            {
                using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
                var ws = await RawWebSocket.ConnectAsync(domain, domain, "/apiws", null, wsCts.Token);
                
                Balancer.Instance.UpdateDomainForDc(dc, baseDomain);
                
                await ws.SendAsync(relayInit, token);
                await BridgeWsReencryptAsync(clientStream, ws, ctx, splitter, clientLabel, $"DC{dc}{(isMedia ? "m" : "")}", token);
                return true;
            }
            catch (Exception ex)
            {
                _logCallback($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} CF proxy failed on {domain}: {ex.Message}");
            }
        }

        return false;
    }

    private async Task<bool> TcpFallbackAsync(
        Stream clientStream, string dst, int port, byte[] relayInit, string clientLabel, CryptoContext ctx, CancellationToken token)
    {
        try
        {
            var tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
            
            await tcpClient.ConnectAsync(dst, port, connectCts.Token);
            
            Stream remoteStream = tcpClient.GetStream();
            await remoteStream.WriteAsync(relayInit.AsMemory(), token);
            await remoteStream.FlushAsync(token);

            await BridgeTcpReencryptAsync(clientStream, remoteStream, ctx, clientLabel, token);
            return true;
        }
        catch (Exception ex)
        {
            _logCallback($"[{clientLabel}] TCP fallback to {dst}:{port} failed: {ex.Message}");
            return false;
        }
    }

    private async Task ProxyToMaskingDomainAsync(Stream clientStream, byte[] initialData, string domain, string clientLabel, CancellationToken token)
    {
        try
        {
            var tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));

            await tcpClient.ConnectAsync(domain, 443, connectCts.Token);
            Stream destStream = tcpClient.GetStream();

            if (initialData.Length > 0)
            {
                await destStream.WriteAsync(initialData.AsMemory(), token);
                await destStream.FlushAsync(token);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            var forwardTask = Task.Run(async () =>
            {
                byte[] buf = new byte[16384];
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        int r = await clientStream.ReadAsync(buf.AsMemory(), cts.Token);
                        if (r == 0) break;
                        await destStream.WriteAsync(buf.AsMemory(0, r), cts.Token);
                        await destStream.FlushAsync(cts.Token);
                    }
                }
                catch {}
                finally { cts.Cancel(); }
            });

            var backwardTask = Task.Run(async () =>
            {
                byte[] buf = new byte[16384];
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        int r = await destStream.ReadAsync(buf.AsMemory(), cts.Token);
                        if (r == 0) break;
                        await clientStream.WriteAsync(buf.AsMemory(0, r), cts.Token);
                        await clientStream.FlushAsync(cts.Token);
                    }
                }
                catch {}
                finally { cts.Cancel(); }
            });

            await Task.WhenAny(forwardTask, backwardTask);
            cts.Cancel();
            try { await Task.WhenAll(forwardTask, backwardTask); } catch {}
        }
        catch (Exception ex)
        {
            _logCallback($"[{clientLabel}] masking proxy failed: {ex.Message}");
        }
    }

    private static (int DcId, bool IsMedia, byte[] ProtoTag, byte[] DecPrekeyIv)? TryHandshake(byte[] handshake, byte[] secret)
    {
        byte[] decPrekeyIv = new byte[Constants.PREKEY_LEN + Constants.IV_LEN];
        Array.Copy(handshake, Constants.SKIP_LEN, decPrekeyIv, 0, decPrekeyIv.Length);

        byte[] decPrekey = new byte[Constants.PREKEY_LEN];
        byte[] decIv = new byte[Constants.IV_LEN];
        Array.Copy(decPrekeyIv, 0, decPrekey, 0, Constants.PREKEY_LEN);
        Array.Copy(decPrekeyIv, Constants.PREKEY_LEN, decIv, 0, Constants.IV_LEN);

        byte[] decKey;
        using (var ms = new MemoryStream())
        {
            ms.Write(decPrekey, 0, decPrekey.Length);
            ms.Write(secret, 0, secret.Length);
            decKey = SHA256.HashData(ms.ToArray());
        }

        byte[] decrypted;
        using (var aes = new AesCtr(decKey, decIv))
        {
            decrypted = aes.Transform(handshake);
        }

        byte[] protoTag = new byte[4];
        Array.Copy(decrypted, Constants.PROTO_TAG_POS, protoTag, 0, 4);

        uint protoInt = BitConverter.ToUInt32(protoTag, 0);
        if (protoInt != Constants.PROTO_ABRIDGED_INT &&
            protoInt != Constants.PROTO_INTERMEDIATE_INT &&
            protoInt != Constants.PROTO_PADDED_INTERMEDIATE_INT)
        {
            return null;
        }

        short dcIdx = BitConverter.ToInt16(decrypted, Constants.DC_IDX_POS);
        int dcId = Math.Abs(dcIdx);
        bool isMedia = dcIdx < 0;

        return (dcId, isMedia, protoTag, decPrekeyIv);
    }

    private static byte[] GenerateRelayInit(byte[] protoTag, int dcIdx)
    {
        byte[] rnd = new byte[Constants.HANDSHAKE_LEN];
        while (true)
        {
            RandomNumberGenerator.Fill(rnd);
            if (Constants.RESERVED_FIRST_BYTES.Contains(rnd[0]))
                continue;

            byte[] start4 = new byte[4];
            Array.Copy(rnd, 0, start4, 0, 4);
            bool reservedStart = false;
            foreach (var r in Constants.RESERVED_STARTS)
            {
                if (r.SequenceEqual(start4))
                {
                    reservedStart = true;
                    break;
                }
            }
            if (reservedStart)
                continue;

            byte[] cont4 = new byte[4];
            Array.Copy(rnd, 4, cont4, 0, 4);
            if (cont4.SequenceEqual(Constants.RESERVED_CONTINUE))
                continue;

            break;
        }

        byte[] encKey = new byte[32];
        byte[] encIv = new byte[16];
        Array.Copy(rnd, Constants.SKIP_LEN, encKey, 0, 32);
        Array.Copy(rnd, Constants.SKIP_LEN + Constants.PREKEY_LEN, encIv, 0, 16);

        byte[] encryptedFull;
        using (var aes = new AesCtr(encKey, encIv))
        {
            encryptedFull = aes.Transform(rnd);
        }

        byte[] dcBytes = BitConverter.GetBytes((short)dcIdx);
        byte[] tailPlain = new byte[8];
        Array.Copy(protoTag, 0, tailPlain, 0, 4);
        Array.Copy(dcBytes, 0, tailPlain, 4, 2);
        byte[] randBytes2 = new byte[2];
        RandomNumberGenerator.Fill(randBytes2);
        Array.Copy(randBytes2, 0, tailPlain, 6, 2);

        byte[] keystreamTail = new byte[8];
        for (int i = 0; i < 8; i++)
        {
            keystreamTail[i] = (byte)(encryptedFull[56 + i] ^ rnd[56 + i]);
        }

        byte[] encryptedTail = new byte[8];
        for (int i = 0; i < 8; i++)
        {
            encryptedTail[i] = (byte)(tailPlain[i] ^ keystreamTail[i]);
        }

        byte[] result = (byte[])rnd.Clone();
        Array.Copy(encryptedTail, 0, result, Constants.PROTO_TAG_POS, 8);
        return result;
    }

    private static CryptoContext BuildCryptoContext(byte[] clientDecPrekeyIv, byte[] secret, byte[] relayInit)
    {
        byte[] cltDecPrekey = new byte[32];
        byte[] cltDecIv = new byte[16];
        Array.Copy(clientDecPrekeyIv, 0, cltDecPrekey, 0, 32);
        Array.Copy(clientDecPrekeyIv, 32, cltDecIv, 0, 16);

        byte[] cltDecKeyBytes;
        using (var ms = new MemoryStream())
        {
            ms.Write(cltDecPrekey, 0, cltDecPrekey.Length);
            ms.Write(secret, 0, secret.Length);
            cltDecKeyBytes = SHA256.HashData(ms.ToArray());
        }

        byte[] cltEncPrekeyIv = (byte[])clientDecPrekeyIv.Clone();
        Array.Reverse(cltEncPrekeyIv);

        byte[] cltEncPrekey = new byte[32];
        byte[] cltEncIv = new byte[16];
        Array.Copy(cltEncPrekeyIv, 0, cltEncPrekey, 0, 32);
        Array.Copy(cltEncPrekeyIv, 32, cltEncIv, 0, 16);

        byte[] cltEncKeyBytes;
        using (var ms = new MemoryStream())
        {
            ms.Write(cltEncPrekey, 0, cltEncPrekey.Length);
            ms.Write(secret, 0, secret.Length);
            cltEncKeyBytes = SHA256.HashData(ms.ToArray());
        }

        var cltDec = new AesCtr(cltDecKeyBytes, cltDecIv);
        var cltEnc = new AesCtr(cltEncKeyBytes, cltEncIv);

        byte[] zero64 = new byte[64];
        cltDec.Transform(zero64);

        byte[] relayEncKey = new byte[32];
        byte[] relayEncIv = new byte[16];
        Array.Copy(relayInit, 8, relayEncKey, 0, 32);
        Array.Copy(relayInit, 40, relayEncIv, 0, 16);

        byte[] relayDecPrekeyIv = new byte[48];
        Array.Copy(relayInit, 8, relayDecPrekeyIv, 0, 48);
        Array.Reverse(relayDecPrekeyIv);

        byte[] relayDecKey = new byte[32];
        byte[] relayDecIv = new byte[16];
        Array.Copy(relayDecPrekeyIv, 0, relayDecKey, 0, 32);
        Array.Copy(relayDecPrekeyIv, 32, relayDecIv, 0, 16);

        var tgEnc = new AesCtr(relayEncKey, relayEncIv);
        var tgDec = new AesCtr(relayDecKey, relayDecIv);

        tgEnc.Transform(zero64);

        return new CryptoContext(cltDec, cltEnc, tgEnc, tgDec);
    }

    private async Task BridgeWsReencryptAsync(Stream clientStream, RawWebSocket ws, CryptoContext ctx, MsgSplitter? splitter, string clientLabel, string dcTag, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        
        var uploadTask = Task.Run(async () =>
        {
            byte[] buffer = new byte[65536];
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int read = await clientStream.ReadAsync(buffer.AsMemory(), cts.Token);
                    if (read == 0)
                    {
                        if (splitter != null)
                        {
                            var tail = splitter.Flush();
                            if (tail.Count > 0)
                            {
                                await ws.SendAsync(tail[0], cts.Token);
                            }
                        }
                        break;
                    }

                    byte[] plain = new byte[read];
                    ctx.ClientDecrypt.Transform(buffer.AsSpan(0, read), plain);

                    byte[] cipher = new byte[read];
                    ctx.TgEncrypt.Transform(plain, cipher);

                    if (splitter != null)
                    {
                        var parts = splitter.Split(cipher);
                        if (parts.Count == 0) continue;
                        if (parts.Count > 1)
                        {
                            await ws.SendBatchAsync(parts, cts.Token);
                        }
                        else
                        {
                            await ws.SendAsync(parts[0], cts.Token);
                        }
                    }
                    else
                    {
                        await ws.SendAsync(cipher, cts.Token);
                    }
                }
            }
            catch (Exception ex)
            {
                _logCallback($"[{clientLabel}] client upload failed: {ex.Message}");
            }
            finally
            {
                cts.Cancel();
            }
        });

        var downloadTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    byte[]? data = await ws.RecvAsync(cts.Token);
                    if (data == null)
                    {
                        break;
                    }

                    byte[] plain = new byte[data.Length];
                    ctx.TgDecrypt.Transform(data, plain);

                    byte[] cipher = new byte[data.Length];
                    ctx.ClientEncrypt.Transform(plain, cipher);

                    await clientStream.WriteAsync(cipher.AsMemory(), cts.Token);
                    await clientStream.FlushAsync(cts.Token);
                }
            }
            catch (Exception ex)
            {
                _logCallback($"[{clientLabel}] upstream download failed: {ex.Message}");
            }
            finally
            {
                cts.Cancel();
            }
        });

        await Task.WhenAny(uploadTask, downloadTask);
        cts.Cancel();
        try { await Task.WhenAll(uploadTask, downloadTask); } catch { }
    }

    private async Task BridgeTcpReencryptAsync(Stream clientStream, Stream remoteStream, CryptoContext ctx, string clientLabel, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        var uploadTask = Task.Run(async () =>
        {
            byte[] buffer = new byte[65536];
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int read = await clientStream.ReadAsync(buffer.AsMemory(), cts.Token);
                    if (read == 0) break;

                    byte[] plain = new byte[read];
                    ctx.ClientDecrypt.Transform(buffer.AsSpan(0, read), plain);

                    byte[] cipher = new byte[read];
                    ctx.TgEncrypt.Transform(plain, cipher);

                    await remoteStream.WriteAsync(cipher.AsMemory(), cts.Token);
                    await remoteStream.FlushAsync(cts.Token);
                }
            }
            catch {}
            finally { cts.Cancel(); }
        });

        var downloadTask = Task.Run(async () =>
        {
            byte[] buffer = new byte[65536];
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int read = await remoteStream.ReadAsync(buffer.AsMemory(), cts.Token);
                    if (read == 0) break;

                    byte[] plain = new byte[read];
                    ctx.TgDecrypt.Transform(buffer.AsSpan(0, read), plain);

                    byte[] cipher = new byte[read];
                    ctx.ClientEncrypt.Transform(plain, cipher);

                    await clientStream.WriteAsync(cipher.AsMemory(), cts.Token);
                    await clientStream.FlushAsync(cts.Token);
                }
            }
            catch {}
            finally { cts.Cancel(); }
        });

        await Task.WhenAny(uploadTask, downloadTask);
        cts.Cancel();
        try { await Task.WhenAll(uploadTask, downloadTask); } catch { }
    }
}

public class CryptoContext : IDisposable
{
    public AesCtr ClientDecrypt { get; }
    public AesCtr ClientEncrypt { get; }
    public AesCtr TgEncrypt { get; }
    public AesCtr TgDecrypt { get; }

    public CryptoContext(AesCtr cltDec, AesCtr cltEnc, AesCtr tgEnc, AesCtr tgDec)
    {
        ClientDecrypt = cltDec;
        ClientEncrypt = cltEnc;
        TgEncrypt = tgEnc;
        TgDecrypt = tgDec;
    }

    public void Dispose()
    {
        ClientDecrypt.Dispose();
        ClientEncrypt.Dispose();
        TgEncrypt.Dispose();
        TgDecrypt.Dispose();
    }
}
