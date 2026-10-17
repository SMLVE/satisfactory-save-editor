using System.IO.Compression;
using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Compression;

/// <summary>
/// Unreal package-style chunked zlib stream used for the save body.
/// Each chunk: magic, archive marker, max chunk size, compressor info, then sizes and zlib data.
/// </summary>
public static class ChunkedZlib
{
    public const uint PackageSignature = 0x9E2A83C1;
    public const uint ArchiveV2Marker = 0x22222222;
    public const int DefaultMaxChunkSize = 128 * 1024;

    /// <summary>Opaque bytes between the archive marker and the size fields (max chunk size + compressor info).</summary>
    public sealed record ChunkFormat(uint MaxChunkSize, byte[] CompressorInfo);

    public static byte[] Decompress(SaveReader r, out ChunkFormat format)
    {
        format = null!;
        using var output = new MemoryStream();
        while (!r.AtEnd)
        {
            uint magic = r.ReadUInt32();
            if (magic != PackageSignature)
                throw new SaveFormatException($"Bad chunk signature 0x{magic:X8} at offset {r.Position - 4}.");
            uint marker = r.ReadUInt32();
            if (marker != ArchiveV2Marker)
                throw new SaveFormatException($"Unexpected chunk archive marker 0x{marker:X8} at offset {r.Position - 4}.");
            uint maxChunkSize = r.ReadUInt32();
            byte[] compressorInfo = r.ReadBytes(5);
            format ??= new ChunkFormat(maxChunkSize, compressorInfo);

            long compressedSize = r.ReadInt64();
            long uncompressedSize = r.ReadInt64();
            long compressedSize2 = r.ReadInt64();
            long uncompressedSize2 = r.ReadInt64();
            if (compressedSize != compressedSize2 || uncompressedSize != uncompressedSize2)
                throw new SaveFormatException($"Chunk size fields disagree at offset {r.Position - 32}.");

            byte[] compressed = r.ReadBytes(checked((int)compressedSize));
            long before = output.Length;
            using (var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
                z.CopyTo(output);
            if (output.Length - before != uncompressedSize)
                throw new SaveFormatException(
                    $"Chunk decompressed to {output.Length - before} bytes, expected {uncompressedSize}.");
        }
        if (format is null)
            throw new SaveFormatException("Save body contains no compressed chunks.");
        return output.ToArray();
    }

    public static void Compress(ReadOnlySpan<byte> body, ChunkFormat format, SaveWriter w,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        int chunkSize = (int)format.MaxChunkSize;
        for (int offset = 0; offset < body.Length; offset += chunkSize)
        {
            var slice = body.Slice(offset, Math.Min(chunkSize, body.Length - offset));
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, level, leaveOpen: true))
                z.Write(slice);
            var compressed = ms.GetBuffer().AsSpan(0, (int)ms.Length);

            w.WriteUInt32(PackageSignature);
            w.WriteUInt32(ArchiveV2Marker);
            w.WriteUInt32(format.MaxChunkSize);
            w.WriteBytes(format.CompressorInfo);
            w.WriteInt64(compressed.Length);
            w.WriteInt64(slice.Length);
            w.WriteInt64(compressed.Length);
            w.WriteInt64(slice.Length);
            w.WriteBytes(compressed);
        }
    }
}
