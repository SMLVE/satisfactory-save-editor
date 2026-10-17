using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Tests;

public class RoundTripTests
{
    [Theory]
    [MemberData(nameof(SampleSaves.All), MemberType = typeof(SampleSaves))]
    public void Body_RoundTrips_ByteIdentically(string sample)
    {
        var original = SampleSaves.OriginalBody(sample);
        var save = SaveFile.Load(SampleSaves.Bytes(sample));

        var rewritten = save.SerializeBody();

        AssertSameBytes(original, rewritten);
    }

    [Theory]
    [MemberData(nameof(SampleSaves.All), MemberType = typeof(SampleSaves))]
    public void Header_RoundTrips_ByteIdentically(string sample)
    {
        var bytes = SampleSaves.Bytes(sample);
        var reader = new SaveReader(bytes);
        var header = SaveHeader.Read(reader);

        var w = new SaveWriter();
        header.Write(w);

        AssertSameBytes(bytes.AsSpan(0, reader.Position).ToArray(), w.ToArray());
    }

    [Theory]
    [MemberData(nameof(SampleSaves.All), MemberType = typeof(SampleSaves))]
    public void SavedFile_Reloads_WithIdenticalBody(string sample)
    {
        var save = SaveFile.Load(SampleSaves.Bytes(sample));
        var expectedBody = save.SerializeBody();

        var reloaded = SaveFile.Load(save.ToBytes());

        Assert.Equal(save.Header.SessionName, reloaded.Header.SessionName);
        Assert.Equal(save.Header.SaveVersion, reloaded.Header.SaveVersion);
        AssertSameBytes(expectedBody, reloaded.SerializeBody());
    }

    private static void AssertSameBytes(byte[] expected, byte[] actual)
    {
        int common = expected.AsSpan().CommonPrefixLength(actual);
        if (common == expected.Length && expected.Length == actual.Length)
            return;
        Assert.Fail($"Bytes differ at offset {common} (expected length {expected.Length}, actual length {actual.Length}).");
    }
}
