using System;
using System.Collections.Generic;
using System.IO;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class MsgSplitter : IDisposable
{
    private readonly uint _proto;
    private readonly MemoryStream _buffer = new();
    private const int MAX_BUFFER_SIZE = 16 * 1024 * 1024; // 16 MB max safety limit

    public MsgSplitter(uint proto)
    {
        _proto = proto;
    }

    public List<byte[]> ProcessAndEncrypt(ReadOnlySpan<byte> plainChunk, AesCtr tgEncrypt)
    {
        var parts = new List<byte[]>();
        if (plainChunk.IsEmpty)
            return parts;

        _buffer.Write(plainChunk);

        byte[] rawBuf = _buffer.GetBuffer();
        int totalLen = (int)_buffer.Length;
        int offset = 0;

        while (offset < totalLen)
        {
            int avail = totalLen - offset;
            int? packetLen = NextPacketLen(rawBuf, offset, avail);

            if (packetLen == null)
            {
                // Incomplete packet: await further TCP stream bytes
                break;
            }

            if (packetLen.Value <= 0 || packetLen.Value > MAX_BUFFER_SIZE)
            {
                // Malformed packet length or overflow: flush remaining buffer to keep connection responsive
                int remaining = totalLen - offset;
                byte[] raw = new byte[remaining];
                Array.Copy(rawBuf, offset, raw, 0, remaining);
                tgEncrypt.Transform(raw, raw);
                parts.Add(raw);
                offset = totalLen;
                break;
            }

            byte[] packet = new byte[packetLen.Value];
            Array.Copy(rawBuf, offset, packet, 0, packetLen.Value);
            tgEncrypt.Transform(packet, packet);
            parts.Add(packet);
            offset += packetLen.Value;
        }

        if (offset > 0)
        {
            int remaining = totalLen - offset;
            if (remaining > 0)
            {
                Buffer.BlockCopy(rawBuf, offset, rawBuf, 0, remaining);
                _buffer.SetLength(remaining);
                _buffer.Position = remaining;
            }
            else
            {
                _buffer.SetLength(0);
                _buffer.Position = 0;
            }
        }

        return parts;
    }

    public List<byte[]> FlushAndEncrypt(AesCtr tgEncrypt)
    {
        var parts = new List<byte[]>();
        int totalLen = (int)_buffer.Length;
        if (totalLen > 0)
        {
            byte[] rawBuf = _buffer.GetBuffer();
            byte[] packet = new byte[totalLen];
            Array.Copy(rawBuf, 0, packet, 0, totalLen);
            tgEncrypt.Transform(packet, packet);
            parts.Add(packet);
            _buffer.SetLength(0);
            _buffer.Position = 0;
        }
        return parts;
    }

    private int? NextPacketLen(byte[] buf, int offset, int avail)
    {
        if (avail <= 0) return null;

        if (_proto == Constants.PROTO_ABRIDGED_INT)
        {
            return NextAbridgedLen(buf, offset, avail);
        }

        if (_proto == Constants.PROTO_INTERMEDIATE_INT || _proto == Constants.PROTO_PADDED_INTERMEDIATE_INT)
        {
            return NextIntermediateLen(buf, offset, avail);
        }

        return avail; // Unknown protocol framing: treat available as a whole frame
    }

    private static int? NextAbridgedLen(byte[] buf, int offset, int avail)
    {
        byte first = buf[offset];
        int headerLen;
        int payloadLen;

        if (first == 0x7F)
        {
            if (avail < 4) return null;

            int lenWords = buf[offset + 1] |
                           (buf[offset + 2] << 8) |
                           (buf[offset + 3] << 16);
            payloadLen = lenWords * 4;
            headerLen = 4;
        }
        else if (first >= 1 && first <= 0x7E)
        {
            payloadLen = first * 4;
            headerLen = 1;
        }
        else
        {
            // Invalid length indicator in Abridged mode
            return 0;
        }

        if (payloadLen <= 0 || payloadLen > MAX_BUFFER_SIZE) return 0;

        int packetLen = headerLen + payloadLen;
        if (avail < packetLen) return null;

        return packetLen;
    }

    private static int? NextIntermediateLen(byte[] buf, int offset, int avail)
    {
        if (avail < 4) return null;

        int payloadLen = (int)((buf[offset] |
                               (buf[offset + 1] << 8) |
                               (buf[offset + 2] << 16) |
                               (buf[offset + 3] << 24)) & 0x7FFFFFFF);

        if (payloadLen <= 0 || payloadLen > MAX_BUFFER_SIZE) return 0;

        int packetLen = 4 + payloadLen;
        if (avail < packetLen) return null;

        return packetLen;
    }

    public void Dispose()
    {
        _buffer.Dispose();
    }
}
