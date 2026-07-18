using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
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
        tcpClient.ReceiveBufferSize = 256 * 1024;
        tcpClient.SendBufferSize = 256 * 1024;

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

        var reader = new StreamReader(sslStream, Encoding.UTF8);
        var responseLines = new System.Collections.Generic.List<string>();
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
                break;
            responseLines.Add(line.Trim());
        }

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

    private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (IsClosed) throw new Exception("WebSocket is closed.");
        byte[] frame = BuildFrame(OP_BINARY, data, mask: true);
        await _sslStream.WriteAsync(frame.AsMemory(), cancellationToken);
        await _sslStream.FlushAsync(cancellationToken);
    }

    public async Task SendBatchAsync(System.Collections.Generic.List<byte[]> parts, CancellationToken cancellationToken = default)
    {
        if (IsClosed) throw new Exception("WebSocket is closed.");
        foreach (var part in parts)
        {
            byte[] frame = BuildFrame(OP_BINARY, part, mask: true);
            await _sslStream.WriteAsync(frame.AsMemory(), cancellationToken);
        }
        await _sslStream.FlushAsync(cancellationToken);
    }

    public async Task<byte[]?> RecvAsync(CancellationToken cancellationToken = default)
    {
        while (!IsClosed)
        {
            var frame = await ReadFrameAsync(cancellationToken);
            if (frame == null) return null;

            byte opcode = frame.Value.Opcode;
            byte[] payload = frame.Value.Payload;

            if (opcode == OP_CLOSE)
            {
                IsClosed = true;
                try
                {
                    byte[] reply = BuildFrame(OP_CLOSE, payload.Length >= 2 ? new byte[] { payload[0], payload[1] } : Array.Empty<byte>(), mask: true);
                    await _sslStream.WriteAsync(reply.AsMemory(), cancellationToken);
                    await _sslStream.FlushAsync(cancellationToken);
                }
                catch { }
                return null;
            }

            if (opcode == OP_PING)
            {
                try
                {
                    byte[] reply = BuildFrame(OP_PONG, payload, mask: true);
                    await _sslStream.WriteAsync(reply.AsMemory(), cancellationToken);
                    await _sslStream.FlushAsync(cancellationToken);
                }
                catch { }
                continue;
            }

            if (opcode == OP_PONG)
            {
                continue;
            }

            if (opcode == 0x1 || opcode == 0x2)
            {
                return payload;
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
            await _sslStream.WriteAsync(frame.AsMemory());
            await _sslStream.FlushAsync();
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

    private async Task<(byte Opcode, byte[] Payload)?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[2];
        int read = 0;
        while (read < 2)
        {
            int r = await _sslStream.ReadAsync(header.AsMemory(read, 2 - read), cancellationToken);
            if (r == 0) return null;
            read += r;
        }

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

        return (opcode, payload);
    }

    private static void XorMask(ReadOnlySpan<byte> data, byte[] mask, Span<byte> output)
    {
        for (int i = 0; i < data.Length; i++)
        {
            output[i] = (byte)(data[i] ^ mask[i % 4]);
        }
    }

    public void Dispose()
    {
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
