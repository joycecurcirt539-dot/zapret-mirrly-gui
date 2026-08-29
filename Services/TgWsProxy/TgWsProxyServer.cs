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
    private readonly string _fakeTlsDomain;
    private readonly bool _forceTestDc;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly Action<string> _logCallback;
    private volatile bool _isStopped;

    private readonly WsPool _wsPool;
    private readonly ConcurrentDictionary<string, DateTime> _dcFailUntil = new();
    private readonly ConcurrentDictionary<string, DateTime> _ipFailUntil = new();

    private const double IP_FAIL_COOLDOWN = 3600.0;
    private const double DC_FAIL_COOLDOWN = 60.0;
    private const double WS_FAIL_TIMEOUT = 3.0;

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
        _fakeTlsDomain = fakeTlsDomain?.Trim() ?? "";
        _forceTestDc = forceTestDc;
        _logCallback = logCallback;

        _wsPool = new WsPool(msg => SafeLog(msg));
    }

    public int PoolSize
    {
        get => _wsPool.PoolSize;
        set => _wsPool.PoolSize = value;
    }

    private void SafeLog(string message)
    {
        if (_isStopped || _cts?.IsCancellationRequested == true)
            return;
        _logCallback(message);
    }

    public void Start()
    {
        _isStopped = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _wsPool.Reset();
        _dcFailUntil.Clear();
        _ipFailUntil.Clear();

        IpBenchmarkPool.Instance.Start(msg => SafeLog(msg));

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

        SafeLog("============================================================");
        SafeLog("  Telegram MTProto WS Bridge Proxy (In-Process C#)");
        SafeLog($"  Listening on   {_host}:{_port}");
        SafeLog($"  Secret:        {Convert.ToHexString(_secretBytes).ToLowerInvariant()}");
        if (!string.IsNullOrEmpty(_fakeTlsDomain))
        {
            SafeLog($"  Fake TLS:      {_fakeTlsDomain}");
        }
        SafeLog("  Target Telegram Anycast IPs:");
        foreach (var dc in _dcRedirects.Keys.OrderBy(k => k))
        {
            SafeLog($"    DC{dc}: {_dcRedirects[dc]}");
        }
        SafeLog("============================================================");

        // Warm up pools
        _wsPool.Warmup(_dcRedirects, token);

        // Accept client connections loop
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_isStopped)
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
                    if (!token.IsCancellationRequested && !_isStopped)
                    {
                        SafeLog($"[TG_SERVER ERROR] Accept connection failed: {ex.Message}");
                    }
                }
            }
        }, token);
    }

    public void Stop()
    {
        _isStopped = true;
        try
        {
            _cts?.Cancel();
        }
        catch { }

        try
        {
            _listener?.Stop();
        }
        catch { }

        IpBenchmarkPool.Instance.Stop();
        _wsPool.Reset();

        _logCallback("[TG_SERVER] Server stopped.");
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        if (_isStopped || token.IsCancellationRequested)
        {
            client.Dispose();
            return;
        }

        string clientLabel = client.Client.RemoteEndPoint?.ToString() ?? "?";
        bool isRealSession = false;

        client.NoDelay = true;
        client.ReceiveBufferSize = 2 * 1024 * 1024;
        client.SendBufferSize = 2 * 1024 * 1024;

        Stream clientStream = client.GetStream();
        CryptoContext? cryptoCtx = null;
        MsgSplitter? splitter = null;

        try
        {
            byte[] handshake = new byte[Constants.HANDSHAKE_LEN];
            byte[] first5 = new byte[5];
            int first5Read = 0;
            while (first5Read < 5)
            {
                int r = await clientStream.ReadAsync(first5.AsMemory(first5Read, 5 - first5Read), token);
                if (r == 0) return; // Silent health/latency probe
                first5Read += r;
            }

            if (first5[0] == 0x16) // TLS Handshake (FakeTLS)
            {
                ushort recordLen = (ushort)((first5[3] << 8) | first5[4]);
                byte[] clientHello = new byte[5 + recordLen];
                Array.Copy(first5, 0, clientHello, 0, 5);

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
                    if (!string.IsNullOrEmpty(_fakeTlsDomain))
                    {
                        SafeLog($"[{clientLabel}] Fake TLS verification failed -> masking proxy to {_fakeTlsDomain}:443");
                        await ProxyToMaskingDomainAsync(clientStream, clientHello, _fakeTlsDomain, clientLabel, token);
                    }
                    else
                    {
                        SafeLog($"[{clientLabel}] Fake TLS verification failed (invalid secret/sni).");
                    }
                    return;
                }

                isRealSession = true;
                SafeLog($"[{clientLabel}] Fake TLS Handshake OK (TS: {tlsResult.Value.Timestamp}).");
                
                byte[] serverHello = FakeTls.BuildServerHello(_secretBytes, tlsResult.Value.ClientRandom, tlsResult.Value.SessionId);
                await clientStream.WriteAsync(serverHello.AsMemory(), token);
                await clientStream.FlushAsync(token);

                clientStream = new FakeTlsStream(clientStream);

                int hsRead = 0;
                while (hsRead < Constants.HANDSHAKE_LEN)
                {
                    int r = await clientStream.ReadAsync(handshake.AsMemory(hsRead, Constants.HANDSHAKE_LEN - hsRead), token);
                    if (r == 0) return;
                    hsRead += r;
                }
            }
            else
            {
                // Raw MTProto Handshake
                Array.Copy(first5, 0, handshake, 0, 5);
                int hsRead = 5;
                while (hsRead < Constants.HANDSHAKE_LEN)
                {
                    int r = await clientStream.ReadAsync(handshake.AsMemory(hsRead, Constants.HANDSHAKE_LEN - hsRead), token);
                    if (r == 0) return;
                    hsRead += r;
                }

                isRealSession = true;
                SafeLog($"[{clientLabel}] Raw MTProto Handshake received.");
            }

            var hsResult = TryHandshake(handshake, _secretBytes);
            if (hsResult == null)
            {
                SafeLog($"[{clientLabel}] Bad MTProto handshake (invalid secret/protocol).");
                return;
            }

            int dc = hsResult.Value.DcId;
            bool isMedia = hsResult.Value.IsMedia;
            byte[] protoTag = hsResult.Value.ProtoTag;
            byte[] clientDecPrekeyIv = hsResult.Value.DecPrekeyIv;

            int dcIdx = isMedia ? -dc : dc;
            uint protoInt = BitConverter.ToUInt32(protoTag, 0);

            SafeLog($"[{clientLabel}] Handshake OK: DC{dc}{(isMedia ? " media" : "")} proto=0x{protoInt:X8}");

            byte[] relayInit = GenerateRelayInit(protoTag, dcIdx);
            cryptoCtx = BuildCryptoContext(clientDecPrekeyIv, _secretBytes, relayInit);

            string dcKey = $"{dc}{(isMedia ? "m" : "")}";
            string configuredIp = _dcRedirects.TryGetValue(dc, out string? ip) ? ip : "";
            string targetIp = IpBenchmarkPool.Instance.GetBestTargetIp(dc, configuredIp);

            if (string.IsNullOrEmpty(targetIp))
            {
                SafeLog($"[{clientLabel}] DC{dc} не настроен целевой IP.");
                return;
            }

            double wsTimeout = (_dcFailUntil.TryGetValue(dcKey, out DateTime failTime) && DateTime.UtcNow < failTime) ? WS_FAIL_TIMEOUT : 5.0;

            var domains = WsPool.GetWsDomains(dc, isMedia);
            RawWebSocket? ws = null;

            // Try Pool Hit
            ws = await _wsPool.GetAsync(dc, isMedia, targetIp, domains, token);
            if (ws != null)
            {
                SafeLog($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> pool hit via {targetIp}");
            }
            else
            {
                // Connect to WebSocket domains via Anycast target IP
                foreach (string domain in domains)
                {
                    if (_isStopped || token.IsCancellationRequested) break;

                    string url = $"wss://{domain}/apiws";
                    SafeLog($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} -> {url} via {targetIp}");

                    try
                    {
                        using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(wsTimeout));
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, wsCts.Token);
                        ws = await RawWebSocket.ConnectAsync(targetIp, domain, "/apiws", null, linked.Token);
                        break;
                    }
                    catch (WsHandshakeException ex)
                    {
                        SafeLog($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WS handshake error: {ex.Message}");
                    }
                    catch (OperationCanceledException)
                    {
                        if (_isStopped || token.IsCancellationRequested) break;
                        IpBenchmarkPool.Instance.RecordFailure(targetIp);
                        SafeLog($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WS connect timed out via {domain}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (_isStopped || token.IsCancellationRequested) break;
                        IpBenchmarkPool.Instance.RecordFailure(targetIp);
                        SafeLog($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} WS connect failed ({domain}): {ex.Message}");
                    }
                }
            }

            // If WebSocket failed -> Clean close (NO FALLBACK)
            if (ws == null)
            {
                _dcFailUntil[dcKey] = DateTime.UtcNow.AddSeconds(DC_FAIL_COOLDOWN);
                SafeLog($"[{clientLabel}] DC{dc}{(isMedia ? " media" : "")} не удалось подключиться к Telegram Anycast WSS.");
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_isStopped && !token.IsCancellationRequested)
            {
                SafeLog($"[{clientLabel}] unexpected error: {ex.Message}");
            }
        }
        finally
        {
            cryptoCtx?.Dispose();
            splitter?.Dispose();
            try { clientStream.Dispose(); } catch { }
            try { client.Dispose(); } catch { }
            if (isRealSession && !_isStopped && !token.IsCancellationRequested)
            {
                SafeLog($"[{clientLabel}] Connection closed.");
            }
        }
    }

    private async Task ProxyToMaskingDomainAsync(Stream clientStream, byte[] initialData, string domain, string clientLabel, CancellationToken token)
    {
        try
        {
            var tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, connectCts.Token);

            await tcpClient.ConnectAsync(domain, 443, linked.Token);
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
            if (!_isStopped && !token.IsCancellationRequested)
            {
                SafeLog($"[{clientLabel}] masking proxy failed: {ex.Message}");
            }
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
        long lastActivity = Environment.TickCount64;

        var pingTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(15000, cts.Token);
                    if (cts.Token.IsCancellationRequested) break;

                    long idleMs = Environment.TickCount64 - Volatile.Read(ref lastActivity);
                    if (idleMs >= 15000)
                    {
                        try
                        {
                            await ws.SendPingAsync(cts.Token);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                cts.Cancel();
            }
        });

        var uploadTask = Task.Run(async () =>
        {
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int read = await clientStream.ReadAsync(buffer.AsMemory(0, 65536), cts.Token);
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

                    Volatile.Write(ref lastActivity, Environment.TickCount64);

                    Span<byte> chunkSpan = buffer.AsSpan(0, read);
                    ctx.ClientDecrypt.Transform(chunkSpan, chunkSpan);
                    ctx.TgEncrypt.Transform(chunkSpan, chunkSpan);

                    if (splitter != null)
                    {
                        var parts = splitter.Split(chunkSpan.ToArray());
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
                        await ws.SendAsync(chunkSpan.ToArray(), cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cts.Token.IsCancellationRequested && !_isStopped)
                {
                    SafeLog($"[{clientLabel}] client upload ended: {ex.Message}");
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
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

                    Volatile.Write(ref lastActivity, Environment.TickCount64);

                    Span<byte> dataSpan = data.AsSpan();
                    ctx.TgDecrypt.Transform(dataSpan, dataSpan);
                    ctx.ClientEncrypt.Transform(dataSpan, dataSpan);

                    try
                    {
                        await clientStream.WriteAsync(data.AsMemory(), cts.Token);
                        await clientStream.FlushAsync(cts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        if (!cts.Token.IsCancellationRequested && !_isStopped)
                        {
                            SafeLog($"[{clientLabel}] client disconnected during write: {ex.Message}");
                        }
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cts.Token.IsCancellationRequested && !_isStopped)
                {
                    SafeLog($"[{clientLabel}] upstream WS download closed: {ex.Message}");
                }
            }
            finally
            {
                cts.Cancel();
            }
        });

        await Task.WhenAny(uploadTask, downloadTask, pingTask);
        cts.Cancel();
        try { await Task.WhenAll(uploadTask, downloadTask, pingTask); } catch { }
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
