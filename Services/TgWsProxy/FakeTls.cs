using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public static class FakeTls
{
    private static readonly byte[] SERVER_HELLO_TEMPLATE = {
        0x16, 0x03, 0x03, 0x00, 0x7a,
        0x02, 0x00, 0x00, 0x76,
        0x03, 0x03,
        // Server Random (32 bytes at offset 11)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x20, // Session ID Length (32)
        // Session ID (32 bytes at offset 44)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x13, 0x01, 0x00,
        0x00, 0x2e,
        0x00, 0x33, 0x00, 0x24, 0x00, 0x1d, 0x00, 0x20,
        // Public Key (32 bytes at offset 89)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x2b, 0x00, 0x02, 0x03, 0x04
    };

    public static (byte[] ClientRandom, byte[] SessionId, uint Timestamp)? VerifyClientHello(byte[] data, byte[] secret)
    {
        int n = data.Length;
        if (n < 43) return null;
        if (data[0] != 0x16) return null;
        if (data[5] != 0x01) return null;

        byte[] clientRandom = new byte[32];
        Array.Copy(data, 11, clientRandom, 0, 32);

        byte[] zeroed = (byte[])data.Clone();
        for (int i = 0; i < 32; i++)
        {
            zeroed[11 + i] = 0;
        }

        byte[] expected;
        using (var hmac = new HMACSHA256(secret))
        {
            expected = hmac.ComputeHash(zeroed);
        }

        bool sigOk = true;
        for (int i = 0; i < 28; i++)
        {
            if (expected[i] != clientRandom[i])
            {
                sigOk = false;
            }
        }
        if (!sigOk) return null;

        byte[] tsBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            tsBytes[i] = (byte)(clientRandom[28 + i] ^ expected[28 + i]);
        }
        uint timestamp = BitConverter.ToUInt32(tsBytes, 0);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > 120) // TIMESTAMP_TOLERANCE = 120
            return null;

        byte[] sessionId = new byte[32];
        if (n >= 44 + 32 && data[43] == 0x20)
        {
            Array.Copy(data, 44, sessionId, 0, 32);
        }

        return (clientRandom, sessionId, timestamp);
    }

    public static byte[] BuildServerHello(byte[] secret, byte[] clientRandom, byte[] sessionId)
    {
        byte[] sh = (byte[])SERVER_HELLO_TEMPLATE.Clone();
        Array.Copy(sessionId, 0, sh, 44, 32);

        byte[] pubKey = new byte[32];
        RandomNumberGenerator.Fill(pubKey);
        Array.Copy(pubKey, 0, sh, 89, 32);

        byte[] ccs = { 0x14, 0x03, 0x03, 0x00, 0x01, 0x01 };

        int encryptedSize = Random.Shared.Next(1900, 2101);
        byte[] encryptedData = new byte[encryptedSize];
        RandomNumberGenerator.Fill(encryptedData);

        byte[] appRecord = new byte[5 + encryptedSize];
        appRecord[0] = 0x17;
        appRecord[1] = 0x03;
        appRecord[2] = 0x03;
        appRecord[3] = (byte)((encryptedSize >> 8) & 0xFF);
        appRecord[4] = (byte)(encryptedSize & 0xFF);
        Array.Copy(encryptedData, 0, appRecord, 5, encryptedSize);

        byte[] response = new byte[sh.Length + ccs.Length + appRecord.Length];
        Array.Copy(sh, 0, response, 0, sh.Length);
        Array.Copy(ccs, 0, response, sh.Length, ccs.Length);
        Array.Copy(appRecord, 0, response, sh.Length + ccs.Length, appRecord.Length);

        byte[] hmacInput = new byte[clientRandom.Length + response.Length];
        Array.Copy(clientRandom, 0, hmacInput, 0, clientRandom.Length);
        Array.Copy(response, 0, hmacInput, clientRandom.Length, response.Length);

        byte[] serverRandom;
        using (var hmac = new HMACSHA256(secret))
        {
            serverRandom = hmac.ComputeHash(hmacInput);
        }

        Array.Copy(serverRandom, 0, response, 11, 32);

        return response;
    }

    public static byte[] WrapTlsRecord(byte[] data)
    {
        using (var ms = new MemoryStream())
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int chunkSize = Math.Min(data.Length - offset, 16384);
                ms.WriteByte(0x17);
                ms.WriteByte(0x03);
                ms.WriteByte(0x03);
                ms.WriteByte((byte)((chunkSize >> 8) & 0xFF));
                ms.WriteByte((byte)(chunkSize & 0xFF));
                ms.Write(data, offset, chunkSize);
                offset += chunkSize;
            }
            return ms.ToArray();
        }
    }
}

public class FakeTlsStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _readHeaderBuf = new byte[5];
    private byte[] _payloadBuf = Array.Empty<byte>();
    private int _payloadOffset;
    private int _payloadCount;

    public FakeTlsStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_payloadCount == 0)
        {
            bool filled = await FillBufferAsync(cancellationToken);
            if (!filled)
                return 0; // EOF
        }

        int toCopy = Math.Min(count, _payloadCount);
        Array.Copy(_payloadBuf, _payloadOffset, buffer, offset, toCopy);
        _payloadOffset += toCopy;
        _payloadCount -= toCopy;
        return toCopy;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        byte[] raw = new byte[count];
        Array.Copy(buffer, offset, raw, 0, count);
        byte[] wrapped = FakeTls.WrapTlsRecord(raw);
        await _inner.WriteAsync(wrapped.AsMemory(), cancellationToken);
    }

    private async Task<bool> FillBufferAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            int headerRead = 0;
            while (headerRead < 5)
            {
                int r = await _inner.ReadAsync(_readHeaderBuf.AsMemory(headerRead, 5 - headerRead), cancellationToken);
                if (r == 0)
                    return false;
                headerRead += r;
            }

            byte rtype = _readHeaderBuf[0];
            ushort recLen = (ushort)((_readHeaderBuf[3] << 8) | _readHeaderBuf[4]);

            if (rtype == 0x14) // Change Cipher Spec (CCS)
            {
                int discarded = 0;
                byte[] discardBuf = new byte[recLen];
                while (discarded < recLen)
                {
                    int r = await _inner.ReadAsync(discardBuf.AsMemory(discarded, recLen - discarded), cancellationToken);
                    if (r == 0)
                        return false;
                    discarded += r;
                }
                continue;
            }

            if (rtype != 0x17) // Not AppData
            {
                return false;
            }

            if (_payloadBuf.Length < recLen)
            {
                _payloadBuf = new byte[recLen];
            }

            int payloadRead = 0;
            while (payloadRead < recLen)
            {
                int r = await _inner.ReadAsync(_payloadBuf.AsMemory(payloadRead, recLen - payloadRead), cancellationToken);
                if (r == 0)
                    return false;
                payloadRead += r;
            }

            _payloadOffset = 0;
            _payloadCount = recLen;
            return true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
