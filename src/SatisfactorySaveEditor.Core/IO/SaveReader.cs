using System.Buffers.Binary;
using System.Text;

namespace SatisfactorySaveEditor.Core.IO;

/// <summary>
/// Little-endian reader over an in-memory buffer with Unreal-style helpers (FString, etc.).
/// Works on a span-backed array for speed; saves are fully decompressed in memory anyway.
/// </summary>
public sealed class SaveReader
{
    private readonly byte[] _buffer;
    private readonly int _end;

    public SaveReader(byte[] buffer) : this(buffer, 0, buffer.Length) { }

    public SaveReader(byte[] buffer, int offset, int length)
    {
        _buffer = buffer;
        Position = offset;
        _end = offset + length;
    }

    public int Position { get; set; }
    public int Length => _end;
    public bool AtEnd => Position >= _end;
    public int Remaining => _end - Position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
            throw new SaveFormatException($"Attempted to read {count} bytes at offset {Position}, but only {Remaining} remain.");
        var span = new ReadOnlySpan<byte>(_buffer, Position, count);
        Position += count;
        return span;
    }

    public byte ReadByte() => Take(1)[0];
    public sbyte ReadSByte() => (sbyte)Take(1)[0];
    public bool ReadBool32() => ReadInt32() != 0;
    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
    public byte[] ReadBytes(int count) => Take(count).ToArray();
    public Guid ReadGuid() => new(Take(16));

    public ReadOnlySpan<byte> PeekBytes(int count) =>
        new(_buffer, Position, Math.Min(count, Remaining));

    /// <summary>
    /// Reads an Unreal FString: int32 length (negative = UTF-16 chars), including null terminator.
    /// Returns null for length 0 so writers can reproduce it exactly.
    /// </summary>
    public string? ReadFString()
    {
        int length = ReadInt32();
        if (length == 0) return null;
        if (length > 0)
        {
            var bytes = Take(length);
            return Encoding.Latin1.GetString(bytes[..^1]);
        }
        // int.MinValue cannot be negated; any length beyond the buffer is corrupt data.
        if (length == int.MinValue || -length > Remaining / 2)
            throw new SaveFormatException($"FString of {-(long)length} UTF-16 chars at offset {Position - 4} exceeds the remaining {Remaining} bytes.");
        int chars = -length;
        var utf16 = Take(chars * 2);
        return Encoding.Unicode.GetString(utf16[..^2]);
    }

    /// <summary>Creates a sub-reader over the next <paramref name="count"/> bytes and advances past them.</summary>
    public SaveReader Slice(int count)
    {
        if (count < 0 || count > Remaining)
            throw new SaveFormatException($"Slice of {count} bytes at {Position} exceeds remaining {Remaining}.");
        var sub = new SaveReader(_buffer, Position, count);
        Position += count;
        return sub;
    }
}
