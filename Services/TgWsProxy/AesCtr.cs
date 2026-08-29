using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class AesCtr : IDisposable
{
    private const int BATCH_BLOCKS = 64; // 1024 bytes per batch
    private const int BATCH_BYTES = BATCH_BLOCKS * 16;

    private readonly Aes _aes;
    private readonly byte[] _counter;
    private readonly byte[] _counterBatch;
    private readonly byte[] _keystreamBatch;
    private int _keystreamAvail;
    private int _keystreamOffset;

    public AesCtr(byte[] key, byte[] iv)
    {
        if (key == null || (key.Length != 16 && key.Length != 24 && key.Length != 32))
            throw new ArgumentException("Key must be 16, 24, or 32 bytes.");
        if (iv == null || iv.Length != 16)
            throw new ArgumentException("IV must be 16 bytes.");

        _aes = Aes.Create();
        _aes.Key = key;
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;

        _counter = (byte[])iv.Clone();
        _counterBatch = new byte[BATCH_BYTES];
        _keystreamBatch = new byte[BATCH_BYTES];
        _keystreamAvail = 0;
        _keystreamOffset = 0;
    }

    public void Transform(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.Length != output.Length)
            throw new ArgumentException("Input and output spans must have the same length.");

        int offset = 0;
        int length = input.Length;

        // 1. Consume any leftover keystream from previous call
        if (_keystreamAvail > 0)
        {
            int toConsume = Math.Min(_keystreamAvail, length);
            XorSpan(input.Slice(0, toConsume), _keystreamBatch.AsSpan(_keystreamOffset, toConsume), output.Slice(0, toConsume));
            _keystreamOffset += toConsume;
            _keystreamAvail -= toConsume;
            offset += toConsume;
        }

        // 2. Fast Batch Path: Process full 1024-byte batches in a single hardware AES-NI call
        while (offset + BATCH_BYTES <= length)
        {
            FillCounterBatch(_counterBatch, _counter, BATCH_BLOCKS);
            _aes.EncryptEcb(_counterBatch, _keystreamBatch, PaddingMode.None);

            XorSpan(input.Slice(offset, BATCH_BYTES), _keystreamBatch.AsSpan(0, BATCH_BYTES), output.Slice(offset, BATCH_BYTES));
            offset += BATCH_BYTES;
        }

        // 3. Process remaining data (< 1024 bytes)
        if (offset < length)
        {
            int remaining = length - offset;
            int blocksNeeded = (remaining + 15) / 16;
            int bytesToGen = blocksNeeded * 16;

            FillCounterBatch(_counterBatch, _counter, blocksNeeded);
            _aes.EncryptEcb(_counterBatch.AsSpan(0, bytesToGen), _keystreamBatch.AsSpan(0, bytesToGen), PaddingMode.None);

            XorSpan(input.Slice(offset, remaining), _keystreamBatch.AsSpan(0, remaining), output.Slice(offset, remaining));

            _keystreamOffset = remaining;
            _keystreamAvail = bytesToGen - remaining;
        }
    }

    public byte[] Transform(byte[] data)
    {
        var result = new byte[data.Length];
        Transform(data, result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void XorSpan(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> dst)
    {
        int offset = 0;
        int len = a.Length;

        if (Vector.IsHardwareAccelerated && len >= Vector<byte>.Count)
        {
            while (offset + Vector<byte>.Count <= len)
            {
                var va = new Vector<byte>(a.Slice(offset, Vector<byte>.Count));
                var vb = new Vector<byte>(b.Slice(offset, Vector<byte>.Count));
                (va ^ vb).CopyTo(dst.Slice(offset, Vector<byte>.Count));
                offset += Vector<byte>.Count;
            }
        }

        while (offset + 8 <= len)
        {
            ulong ua = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(in a[offset]));
            ulong ub = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(in b[offset]));
            Unsafe.WriteUnaligned(ref dst[offset], ua ^ ub);
            offset += 8;
        }

        while (offset < len)
        {
            dst[offset] = (byte)(a[offset] ^ b[offset]);
            offset++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillCounterBatch(Span<byte> batch, byte[] counter, int blocks)
    {
        for (int b = 0; b < blocks; b++)
        {
            counter.CopyTo(batch.Slice(b * 16, 16));
            IncrementCounter(counter);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IncrementCounter(byte[] counter)
    {
        for (int i = 15; i >= 0; i--)
        {
            if (++counter[i] != 0)
                break;
        }
    }

    public void Dispose()
    {
        _aes.Dispose();
    }
}

