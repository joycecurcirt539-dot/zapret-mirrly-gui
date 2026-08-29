using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class RawWebSocket : IDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly SslStream _sslStream;
    public bool IsClosed { get; private set; }

    public const byte OP_BINARY = 0x2;
    public const byte OP_CLOSE = 0x8;
    public const byte OP_PING = 0x9;
    public const byte OP_PONG = 0xA;

    private RawWebSocket(TcpClient tcpClient, SslStream sslStream)
    {
        _tcpClient = tcpClient;
        _sslStream = sslStream;
    }

    public static async Task<RawWebSocket> ConnectAsync(string host, string domain, string path = "/apiws", string? sni = null, CancellationToken cancellationToken = default)
    {
        sni ??= domain;

        var tcpClient = new TcpClient();
        tcpClient.NoDelay = true;
        tcpClient.ReceiveBufferSize = 2 * 1024 * 1024;
        tcpClient.SendBufferSize = 2 * 1024 * 1024;

        await tcpClient.ConnectAsync(host, 443, cancellationToken);

        var sslStream = new SslStream(tcpClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate));
        
        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = sni,
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
        };

        await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken);

        byte[] nonce = new byte[16];
        RandomNumberGenerator.Fill(nonce);
        string wsKey = Convert.ToBase64String(nonce);

        string req = $"GET {path} HTTP/1.1\r\n" +
                     $"Host: {domain}\r\n" +
                     $"Upgrade: websocket\r\n" +
                     $"Connection: Upgrade\r\n" +
                     $"Sec-WebSocket-Key: {wsKey}\r\n" +
                     $"Sec-WebSocket-Version: 13\r\n" +
                     $"Sec-WebSocket-Protocol: binary\r\n\r\n";

        byte[] reqBytes = Encoding.ASCII.GetBytes(req);
        await sslStream.WriteAsync(reqBytes.AsMemory(), cancellationToken);
        await sslStream.FlushAsync(cancellationToken);

        var responseLines = await ReadHttpResponseHeadersAsync(sslStream, cancellationToken);

        if (responseLines.Count == 0)
        {
            sslStream.Dispose();
            tcpClient.Dispose();
            throw new Exception("Empty response from WebSocket server.");
        }

        string firstLine = responseLines[0];
        string[] parts = firstLine.Split(' ');
        int statusCode = 0;
        if (parts.Length >= 2)
        {
            int.TryParse(parts[1], out statusCode);
        }

        if (statusCode == 101)
        {
            return new RawWebSocket(tcpClient, sslStream);
        }

        var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < responseLines.Count; i++)
        {
            string hl = responseLines[i];
            int colonIdx = hl.IndexOf(':');
            if (colonIdx > 0)
            {
                string k = hl.Substring(0, colonIdx).Trim();
                string v = hl.Substring(colonIdx + 1).Trim();
                headers[k] = v;
            }
        }

        sslStream.Dispose();
        tcpClient.Dispose();

        headers.TryGetValue("Location", out string? location);
        throw new WsHandshakeException(statusCode, firstLine, headers, location);
    }

    private static async Task<System.Collections.Generic.List<string>> ReadHttpResponseHeadersAsync(SslStream sslStream, CancellationToken cancellationToken)
    {
        var lines = new System.Collections.Generic.List<string>();
        var lineBuffer = new System.Collections.Generic.List<byte>(128);
        byte[] oneByte = new byte[1];

        while (true)
        {
            int read = await sslStream.ReadAsync(oneByte.AsMemory(0, 1), cancellationToken);
            if (read == 0) break;

            byte b = oneByte[0];
            if (b == (byte)'\n')
            {
                int count = lineBuffer.Count;
                if (count > 0 && lineBuffer[count - 1] == (byte)'\r')
                {
                    lineBuffer.RemoveAt(count - 1);
                }
                string line = Encoding.UTF8.GetString(lineBuffer.ToArray());
                lineBuffer.Clear();

                if (line.Length == 0)
                {
                    break; // End of HTTP headers (\r\n\r\n)
                }
                lines.Add(line);
            }
            else
            {
                lineBuffer.Add(b);
            }
        }
        return lines;
    }

    private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task SendPingAsync(CancellationToken cancellationToken = default)
    {
        if (IsClosed) throw new Exception("WebSocket is closed.");
        byte[] frame = BuildFrame(OP_PING, Array.Empty<byte>(), mask: true);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _sslStream.WriteAsync(frame.AsMemory(), cancellationToken);
            await _sslStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (IsClosed) throw new Exception("WebSocket is closed.");
        byte[] frame = BuildFrame(OP_BINARY, data, mask: true);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _sslStream.WriteAsync(frame.AsMemory(), cancellationToken);
            await _sslStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendBatchAsync(System.Collections.Generic.List<byte[]> parts, CancellationToken cancellationToken = default)
    {
        if (IsClosed) throw new Exception("WebSocket is closed.");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var part in parts)
            {
                byte[] frame = BuildFrame(OP_BINARY, part, mask: true);
                await _sslStream.WriteAsync(frame.AsMemory(), cancellationToken);
            }
            await _sslStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private MemoryStream? _fragmentBuffer;

    public async Task<byte[]?> RecvAsync(CancellationToken cancellationToken = default)
    {
        while (!IsClosed)
        {
            var frame = await ReadFrameAsync(cancellationToken);
            if (frame == null) return null;

            bool isFin = frame.Value.IsFin;
            byte opcode = frame.Value.Opcode;
            byte[] payload = frame.Value.Payload;

            if (opcode == OP_CLOSE)
            {
                IsClosed = true;
                try
                {
                    byte[] reply = BuildFrame(OP_CLOSE, payload.Length >= 2 ? new byte[] { payload[0], payload[1] } : Array.Empty<byte>(), mask: true);
                    await _writeLock.WaitAsync(cancellationToken);
                    try
                    {
                        await _sslStream.WriteAsync(reply.AsMemory(), cancellationToken);
                        await _sslStream.FlushAsync(cancellationToken);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
                catch { }
                return null;
            }

            if (opcode == OP_PING)
            {
                try
                {
                    byte[] reply = BuildFrame(OP_PONG, payload, mask: true);
                    await _writeLock.WaitAsync(cancellationToken);
                    try
                    {
                        await _sslStream.WriteAsync(reply.AsMemory(), cancellationToken);
                        await _sslStream.FlushAsync(cancellationToken);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
                catch { }
                continue;
            }

            if (opcode == OP_PONG)
            {
                continue;
            }

            if (opcode == 0x1 || opcode == 0x2) // Initial text or binary frame
            {
                if (isFin)
                {
                    return payload;
                }

                _fragmentBuffer?.Dispose();
                _fragmentBuffer = new MemoryStream();
                _fragmentBuffer.Write(payload, 0, payload.Length);
                continue;
            }

            if (opcode == 0x0) // Continuation frame
            {
                if (_fragmentBuffer != null)
                {
                    _fragmentBuffer.Write(payload, 0, payload.Length);
                    if (isFin)
                    {
                        byte[] fullMessage = _fragmentBuffer.ToArray();
                        _fragmentBuffer.Dispose();
                        _fragmentBuffer = null;
                        return fullMessage;
                    }
                }
                continue;
            }
        }

        return null;
    }

    public async Task CloseAsync()
    {
        if (IsClosed) return;
        IsClosed = true;
        try
        {
            byte[] frame = BuildFrame(OP_CLOSE, Array.Empty<byte>(), mask: true);
            await _writeLock.WaitAsync();
            try
            {
                await _sslStream.WriteAsync(frame.AsMemory());
                await _sslStream.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch { }
        finally
        {
            Dispose();
        }
    }

    private static byte[] BuildFrame(byte opcode, byte[] data, bool mask)
    {
        long length = data.Length;
        int headerSize = 2;
        if (length >= 126 && length < 65536) headerSize += 2;
        else if (length >= 65536) headerSize += 8;
        if (mask) headerSize += 4;

        byte[] frame = new byte[headerSize + length];
        frame[0] = (byte)(0x80 | opcode);

        int offset = 2;
        if (length < 126)
        {
            frame[1] = (byte)((byte)length | (mask ? 0x80 : 0x00));
        }
        else if (length < 65536)
        {
            frame[1] = (byte)(126 | (mask ? 0x80 : 0x00));
            frame[2] = (byte)((length >> 8) & 0xFF);
            frame[3] = (byte)(length & 0xFF);
            offset += 2;
        }
        else
        {
            frame[1] = (byte)(127 | (mask ? 0x80 : 0x00));
            for (int i = 7; i >= 0; i--)
            {
                frame[2 + i] = (byte)((length >> (8 * (7 - i))) & 0xFF);
            }
            offset += 8;
        }

        if (mask)
        {
            byte[] maskKey = new byte[4];
            RandomNumberGenerator.Fill(maskKey);
            Array.Copy(maskKey, 0, frame, offset, 4);
            offset += 4;

            XorMask(data, maskKey, frame.AsSpan(offset));
        }
        else
        {
            Array.Copy(data, 0, frame, offset, length);
        }

        return frame;
    }

    private async Task<(bool IsFin, byte Opcode, byte[] Payload)?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[2];
        int read = 0;
        while (read < 2)
        {
            int r = await _sslStream.ReadAsync(header.AsMemory(read, 2 - read), cancellationToken);
            if (r == 0) return null;
            read += r;
        }

        bool isFin = (header[0] & 0x80) != 0;
        byte opcode = (byte)(header[0] & 0x0F);
        bool hasMask = (header[1] & 0x80) != 0;
        long length = header[1] & 0x7F;

        if (length == 126)
        {
            byte[] extLen = new byte[2];
            read = 0;
            while (read < 2)
            {
                int r = await _sslStream.ReadAsync(extLen.AsMemory(read, 2 - read), cancellationToken);
                if (r == 0) return null;
                read += r;
            }
            length = (extLen[0] << 8) | extLen[1];
        }
        else if (length == 127)
        {
            byte[] extLen = new byte[8];
            read = 0;
            while (read < 8)
            {
                int r = await _sslStream.ReadAsync(extLen.AsMemory(read, 8 - read), cancellationToken);
                if (r == 0) return null;
                read += r;
            }
            length = 0;
            for (int i = 0; i < 8; i++)
            {
                length = (length << 8) | extLen[i];
            }
        }

        byte[] maskKey = new byte[4];
        if (hasMask)
        {
            read = 0;
            while (read < 4)
            {
                int r = await _sslStream.ReadAsync(maskKey.AsMemory(read, 4 - read), cancellationToken);
                if (r == 0) return null;
                read += r;
            }
        }

        byte[] payload = new byte[length];
        long payloadRead = 0;
        while (payloadRead < length)
        {
            int toRead = (int)Math.Min(length - payloadRead, 65536);
            int r = await _sslStream.ReadAsync(payload.AsMemory((int)payloadRead, toRead), cancellationToken);
            if (r == 0) return null;
            payloadRead += r;
        }

        if (hasMask)
        {
            XorMask(payload, maskKey, payload);
        }

        return (isFin, opcode, payload);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void XorMask(ReadOnlySpan<byte> data, byte[] mask, Span<byte> output)
    {
        int offset = 0;
        int length = data.Length;

        if (Vector.IsHardwareAccelerated && length >= Vector<byte>.Count)
        {
            byte[] maskVectorBytes = new byte[Vector<byte>.Count];
            for (int i = 0; i < maskVectorBytes.Length; i++) maskVectorBytes[i] = mask[i % 4];
            var maskVector = new Vector<byte>(maskVectorBytes);

            while (offset + Vector<byte>.Count <= length)
            {
                var v = new Vector<byte>(data.Slice(offset, Vector<byte>.Count));
                (v ^ maskVector).CopyTo(output.Slice(offset, Vector<byte>.Count));
                offset += Vector<byte>.Count;
            }
        }

        uint mask4 = (uint)(mask[0] | (mask[1] << 8) | (mask[2] << 16) | (mask[3] << 24));
        ulong mask8 = (ulong)mask4 | ((ulong)mask4 << 32);

        while (offset + 8 <= length)
        {
            ulong in8 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(in data[offset]));
            Unsafe.WriteUnaligned(ref output[offset], in8 ^ mask8);
            offset += 8;
        }

        while (offset < length)
        {
            output[offset] = (byte)(data[offset] ^ mask[offset % 4]);
            offset++;
        }
    }

    public void Dispose()
    {
        _fragmentBuffer?.Dispose();
        _fragmentBuffer = null;
        _writeLock.Dispose();
        _sslStream.Dispose();
        _tcpClient.Dispose();
    }
}

public class WsHandshakeException : Exception
{
    public int StatusCode { get; }
    public string StatusLine { get; }
    public System.Collections.Generic.Dictionary<string, string> Headers { get; }
    public string? Location { get; }

    public WsHandshakeException(int statusCode, string statusLine, System.Collections.Generic.Dictionary<string, string> headers, string? location)
        : base($"HTTP {statusCode}: {statusLine}")
    {
        StatusCode = statusCode;
        StatusLine = statusLine;
        Headers = headers;
        Location = location;
    }

    public bool IsRedirect => StatusCode is 301 or 302 or 303 or 307 or 308;
}
