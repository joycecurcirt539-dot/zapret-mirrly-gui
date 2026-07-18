using System;
using System.Security.Cryptography;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class AesCtr : IDisposable
{
    private readonly Aes _aes;
    private readonly byte[] _counter;
    private readonly byte[] _keystream;
    private int _keystreamUsed;

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
        _keystream = new byte[16];
        _keystreamUsed = 16; // Force initial keystream generation
    }

    public void Transform(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.Length != output.Length)
            throw new ArgumentException("Input and output spans must have the same length.");

        for (int i = 0; i < input.Length; i++)
        {
            if (_keystreamUsed == 16)
            {
                _aes.EncryptEcb(_counter, _keystream, PaddingMode.None);
                IncrementCounter(_counter);
                _keystreamUsed = 0;
            }

            output[i] = (byte)(input[i] ^ _keystream[_keystreamUsed++]);
        }
    }

    public byte[] Transform(byte[] data)
    {
        var result = new byte[data.Length];
        Transform(data, result);
        return result;
    }

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
