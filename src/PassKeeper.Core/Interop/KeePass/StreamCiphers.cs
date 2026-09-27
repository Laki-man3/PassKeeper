using System.Buffers.Binary;
using System.Numerics;

namespace PassKeeper.Core.Interop.KeePass;

internal interface IKeystream
{
    void Xor(Span<byte> data);
}

/// <summary>ChaCha20 (RFC 8439, 96-bit nonce, 32-bit block counter).</summary>
internal sealed class ChaCha20 : IKeystream
{
    private readonly uint[] _state = new uint[16];
    private readonly byte[] _block = new byte[64];
    private int _position = 64;

    public ChaCha20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, uint counter = 0)
    {
        if (key.Length != 32) throw new ArgumentException("ChaCha20 key must be 32 bytes.");
        if (nonce.Length != 12) throw new ArgumentException("ChaCha20 nonce must be 12 bytes.");
        _state[0] = 0x61707865;
        _state[1] = 0x3320646e;
        _state[2] = 0x79622d32;
        _state[3] = 0x6b206574;
        for (var i = 0; i < 8; i++) _state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(i * 4)..]);
        _state[12] = counter;
        for (var i = 0; i < 3; i++) _state[13 + i] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[(i * 4)..]);
    }

    public void Xor(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            if (_position == 64) NextBlock();
            data[i] ^= _block[_position++];
        }
    }

    private void NextBlock()
    {
        Span<uint> x = stackalloc uint[16];
        _state.CopyTo(x);
        for (var i = 0; i < 10; i++)
        {
            Quarter(x, 0, 4, 8, 12);
            Quarter(x, 1, 5, 9, 13);
            Quarter(x, 2, 6, 10, 14);
            Quarter(x, 3, 7, 11, 15);
            Quarter(x, 0, 5, 10, 15);
            Quarter(x, 1, 6, 11, 12);
            Quarter(x, 2, 7, 8, 13);
            Quarter(x, 3, 4, 9, 14);
        }
        for (var i = 0; i < 16; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(_block.AsSpan(i * 4), x[i] + _state[i]);
        _state[12]++;
        if (_state[12] == 0) _state[13]++;
        _position = 0;
    }

    private static void Quarter(Span<uint> x, int a, int b, int c, int d)
    {
        x[a] += x[b]; x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 16);
        x[c] += x[d]; x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 12);
        x[a] += x[b]; x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 8);
        x[c] += x[d]; x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 7);
    }
}

/// <summary>Salsa20/20 with a 256-bit key and 64-bit nonce (KDBX 3.x inner stream).</summary>
internal sealed class Salsa20 : IKeystream
{
    private readonly uint[] _state = new uint[16];
    private readonly byte[] _block = new byte[64];
    private int _position = 64;

    public Salsa20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
    {
        if (key.Length != 32) throw new ArgumentException("Salsa20 key must be 32 bytes.");
        if (nonce.Length != 8) throw new ArgumentException("Salsa20 nonce must be 8 bytes.");
        _state[0] = 0x61707865;
        _state[5] = 0x3320646e;
        _state[10] = 0x79622d32;
        _state[15] = 0x6b206574;
        for (var i = 0; i < 4; i++)
        {
            _state[1 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(i * 4)..]);
            _state[11 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(16 + i * 4)..]);
        }
        _state[6] = BinaryPrimitives.ReadUInt32LittleEndian(nonce);
        _state[7] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[4..]);
        _state[8] = 0;
        _state[9] = 0;
    }

    public void Xor(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            if (_position == 64) NextBlock();
            data[i] ^= _block[_position++];
        }
    }

    private void NextBlock()
    {
        Span<uint> x = stackalloc uint[16];
        _state.CopyTo(x);
        for (var i = 0; i < 10; i++)
        {
            // Column round
            x[4] ^= R(x[0] + x[12], 7); x[8] ^= R(x[4] + x[0], 9); x[12] ^= R(x[8] + x[4], 13); x[0] ^= R(x[12] + x[8], 18);
            x[9] ^= R(x[5] + x[1], 7); x[13] ^= R(x[9] + x[5], 9); x[1] ^= R(x[13] + x[9], 13); x[5] ^= R(x[1] + x[13], 18);
            x[14] ^= R(x[10] + x[6], 7); x[2] ^= R(x[14] + x[10], 9); x[6] ^= R(x[2] + x[14], 13); x[10] ^= R(x[6] + x[2], 18);
            x[3] ^= R(x[15] + x[11], 7); x[7] ^= R(x[3] + x[15], 9); x[11] ^= R(x[7] + x[3], 13); x[15] ^= R(x[11] + x[7], 18);
            // Row round
            x[1] ^= R(x[0] + x[3], 7); x[2] ^= R(x[1] + x[0], 9); x[3] ^= R(x[2] + x[1], 13); x[0] ^= R(x[3] + x[2], 18);
            x[6] ^= R(x[5] + x[4], 7); x[7] ^= R(x[6] + x[5], 9); x[4] ^= R(x[7] + x[6], 13); x[5] ^= R(x[4] + x[7], 18);
            x[11] ^= R(x[10] + x[9], 7); x[8] ^= R(x[11] + x[10], 9); x[9] ^= R(x[8] + x[11], 13); x[10] ^= R(x[9] + x[8], 18);
            x[12] ^= R(x[15] + x[14], 7); x[13] ^= R(x[12] + x[15], 9); x[14] ^= R(x[13] + x[12], 13); x[15] ^= R(x[14] + x[13], 18);
        }
        for (var i = 0; i < 16; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(_block.AsSpan(i * 4), x[i] + _state[i]);
        _state[8]++;
        if (_state[8] == 0) _state[9]++;
        _position = 0;
    }

    private static uint R(uint v, int c) => BitOperations.RotateLeft(v, c);
}
