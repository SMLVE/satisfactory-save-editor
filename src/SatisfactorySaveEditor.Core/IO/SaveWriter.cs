using System.Buffers.Binary;
using System.Text;

namespace SatisfactorySaveEditor.Core.IO;

/// <summary>Little-endian writer mirroring <see cref="SaveReader"/>, with back-patching for size fields.</summary>
public sealed class SaveWriter
{
    private byte[] _buffer;

    public SaveWriter(int initialCapacity = 1 << 16) => _buffer = new byte[initialCapacity];

    public int Position { get; private set; }

    private Span<byte> Reserve(int count)
    {
        if (Position + count > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Position + count));
        var span = new Span<byte>(_buffer, Position, count);
        Position += count;
        return span;
    }

    public void WriteByte(byte value) => Reserve(1)[0] = value;
    public void WriteSByte(sbyte value) => Reserve(1)[0] = (byte)value;
    public void WriteInt16(short value) => BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), value);
    public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
    public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
    public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
    public void WriteInt64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);
    public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
    public void WriteSingle(float value) => BinaryPrimitives.WriteSingleLittleEndian(Reserve(4), value);
    public void WriteDouble(double value) => BinaryPrimitives.WriteDoubleLittleEndian(Reserve(8), value);
    public void WriteBytes(ReadOnlySpan<byte> bytes) => bytes.CopyTo(Reserve(bytes.Length));
    public void WriteGuid(Guid value) => value.TryWriteBytes(Reserve(16));

    /// <summary>Writes an FString the way Unreal does: Latin-1 if all chars fit, otherwise UTF-16. Null = length 0.</summary>
    public void WriteFString(string? value)
    {
        if (value is null)
        {
            WriteInt32(0);
            return;
        }
        bool wide = false;
        foreach (char c in value)
        {
            if (c > 0x7F) { wide = true; break; }
        }
        if (!wide)
        {
            WriteInt32(value.Length + 1);
            var span = Reserve(value.Length + 1);
            Encoding.Latin1.GetBytes(value, span);
            span[^1] = 0;
        }
        else
        {
            WriteInt32(-(value.Length + 1));
            var span = Reserve((value.Length + 1) * 2);
            Encoding.Unicode.GetBytes(value, span);
            span[^2] = 0;
            span[^1] = 0;
        }
    }

    /// <summary>Reserves an int32 placeholder and returns its offset for <see cref="PatchInt32"/>.</summary>
    public int ReserveInt32() { int at = Position; WriteInt32(0); return at; }
    public int ReserveInt64() { int at = Position; WriteInt64(0); return at; }

    public void PatchInt32(int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_buffer, offset, 4), value);

    public void PatchInt64(int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(_buffer, offset, 8), value);

    public ReadOnlySpan<byte> WrittenSpan => new(_buffer, 0, Position);
    public byte[] ToArray() => WrittenSpan.ToArray();
}
