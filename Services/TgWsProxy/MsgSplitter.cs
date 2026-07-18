using System;
using System.Collections.Generic;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class MsgSplitter : IDisposable
{
    private readonly AesCtr _dec;
    private readonly uint _proto;
    private readonly List<byte> _cipherBuf = new();
    private readonly List<byte> _plainBuf = new();
    private bool _disabled;

    public MsgSplitter(byte[] relayInit, uint proto)
    {
        byte[] key = new byte[32];
        byte[] iv = new byte[16];
        Array.Copy(relayInit, 8, key, 0, 32);
        Array.Copy(relayInit, 40, iv, 0, 16);

        _dec = new AesCtr(key, iv);
        
        // Fast-forward decryption context by 64 zero bytes
        byte[] zero64 = new byte[64];
        _dec.Transform(zero64);

        _proto = proto;
    }

    public List<byte[]> Split(ReadOnlySpan<byte> chunk)
    {
        var parts = new List<byte[]>();
        if (chunk.IsEmpty)
            return parts;

        if (_disabled)
        {
            parts.Add(chunk.ToArray());
            return parts;
        }

        foreach (var b in chunk)
        {
            _cipherBuf.Add(b);
        }

        byte[] decrypted = _dec.Transform(chunk.ToArray());
        foreach (var b in decrypted)
        {
            _plainBuf.Add(b);
        }

        int offset = 0;
        while (offset < _cipherBuf.Count)
        {
            int avail = _cipherBuf.Count - offset;
            int? packetLen = NextPacketLen(offset, avail);

            if (packetLen == null)
            {
                break;
            }

            if (packetLen.Value <= 0)
            {
                int remainingLen = _cipherBuf.Count - offset;
                byte[] trailing = new byte[remainingLen];
                _cipherBuf.CopyTo(offset, trailing, 0, remainingLen);
                parts.Add(trailing);
                offset = _cipherBuf.Count;
                _disabled = true;
                break;
            }

            byte[] packet = new byte[packetLen.Value];
            _cipherBuf.CopyTo(offset, packet, 0, packetLen.Value);
            parts.Add(packet);
            offset += packetLen.Value;
        }

        if (offset > 0)
        {
            _cipherBuf.RemoveRange(0, offset);
            _plainBuf.RemoveRange(0, offset);
        }

        return parts;
    }

    public List<byte[]> Flush()
    {
        var parts = new List<byte[]>();
        if (_cipherBuf.Count > 0)
        {
            parts.Add(_cipherBuf.ToArray());
            _cipherBuf.Clear();
            _plainBuf.Clear();
        }
        return parts;
    }

    private int? NextPacketLen(int offset, int avail)
    {
        if (avail <= 0)
            return null;

        if (_proto == Constants.PROTO_ABRIDGED_INT)
        {
            return NextAbridgedLen(offset, avail);
        }
        
        if (_proto == Constants.PROTO_INTERMEDIATE_INT || _proto == Constants.PROTO_PADDED_INTERMEDIATE_INT)
        {
            return NextIntermediateLen(offset, avail);
        }

        return 0;
    }

    private int? NextAbridgedLen(int offset, int avail)
    {
        byte first = _plainBuf[offset];
        int headerLen;
        int payloadLen;

        if (first == 0x7F || first == 0xFF)
        {
            if (avail < 4)
                return null;

            payloadLen = (_plainBuf[offset + 1] | 
                          (_plainBuf[offset + 2] << 8) | 
                          (_plainBuf[offset + 3] << 16)) * 4;
            headerLen = 4;
        }
        else
        {
            payloadLen = (first & 0x7F) * 4;
            headerLen = 1;
        }

        if (payloadLen <= 0)
            return 0;

        int packetLen = headerLen + payloadLen;
        if (avail < packetLen)
            return null;

        return packetLen;
    }

    private int? NextIntermediateLen(int offset, int avail)
    {
        if (avail < 4)
            return null;

        int payloadLen = (_plainBuf[offset] | 
                          (_plainBuf[offset + 1] << 8) | 
                          (_plainBuf[offset + 2] << 16) | 
                          ((_plainBuf[offset + 3] & 0x7F) << 24));
        
        if (payloadLen <= 0)
            return 0;

        int packetLen = 4 + payloadLen;
        if (avail < packetLen)
            return null;

        return packetLen;
    }

    public void Dispose()
    {
        _dec.Dispose();
    }
}
