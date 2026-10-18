using System.Buffers.Binary;
using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Tests;

/// <summary>Damaged or unsupported files must be refused with <see cref="SaveFormatException"/>, never another exception.</summary>
public class LoadErrorTests
{
    /// <summary>Offset of the first compressed chunk (right after the uncompressed header).</summary>
    private static int FirstChunkOffset(byte[] bytes)
    {
        var reader = new SaveReader(bytes);
        SaveHeader.Read(reader);
        return reader.Position;
    }

    [Fact]
    public void CorruptCompressedData_ThrowsSaveFormatException()
    {
        var bytes = SampleSaves.Bytes("sample.sav").ToArray();
        for (int i = bytes.Length - 200; i < bytes.Length - 100; i++)
            bytes[i] ^= 0x5A;

        Assert.Throws<SaveFormatException>(() => SaveFile.Load(bytes));
    }

    [Fact]
    public void OversizedChunkSize_ThrowsSaveFormatException()
    {
        var bytes = SampleSaves.Bytes("sample.sav").ToArray();
        // Chunk layout: magic, archive marker, max chunk size (4 bytes each), 5 bytes of compressor info, then sizes.
        int compressedSizeAt = FirstChunkOffset(bytes) + 17;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(compressedSizeAt), long.MaxValue);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(compressedSizeAt + 16), long.MaxValue);

        Assert.Throws<SaveFormatException>(() => SaveFile.Load(bytes));
    }

    [Fact]
    public void ReadPastEnd_WithHugeCount_ThrowsSaveFormatException()
    {
        var reader = new SaveReader(new byte[16]) { Position = 8 };
        Assert.Throws<SaveFormatException>(() => reader.ReadBytes(int.MaxValue));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1_000_000)]
    public void FStringWithImpossibleLength_ThrowsSaveFormatException(int length)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        Assert.Throws<SaveFormatException>(() => new SaveReader(bytes).ReadFString());
    }

    [Fact]
    public void OlderSaveVersion_IsRefused()
    {
        var bytes = SampleSaves.Bytes("sample.sav").ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 59u); // after the header version

        var ex = Assert.Throws<SaveFormatException>(() => SaveFile.Load(bytes));
        Assert.Contains("older", ex.Message, StringComparison.Ordinal);
    }
}
