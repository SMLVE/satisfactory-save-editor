using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Trailing;

/// <summary>
/// Class-specific data stored after an object's property list, decoded into a typed model.
/// Types without a decoder keep their bytes raw in <see cref="SaveObject.TrailingData"/>.
/// </summary>
public abstract class TrailingValue
{
    internal abstract void Write(SaveWriter w);

    public byte[] ToBytes()
    {
        var w = new SaveWriter(256);
        Write(w);
        return w.ToArray();
    }
}

/// <summary>A power line (wire): the two power connection components it runs between.</summary>
public sealed class PowerLineTrailing : TrailingValue
{
    public ObjectReference Source { get; set; } = ObjectReference.Empty;
    public ObjectReference Target { get; set; } = ObjectReference.Empty;

    internal static PowerLineTrailing Read(SaveReader r) => new() { Source = ObjectReference.Read(r), Target = ObjectReference.Read(r) };

    internal override void Write(SaveWriter w)
    {
        Source.Write(w);
        Target.Write(w);
    }

    public override string ToString() => $"{Source.PathName} ↔ {Target.PathName}";
}

/// <summary>Chooses and verifies trailing-data decoders.</summary>
public static class TrailingDecoders
{
    /// <summary>
    /// Decodes trailing bytes when a decoder exists for the object and reproduces the bytes exactly;
    /// returns null otherwise so the bytes stay raw.
    /// </summary>
    public static TrailingValue? TryDecode(SaveObject obj, byte[] bytes)
    {
        if (bytes.Length == 0)
            return null;
        try
        {
            var reader = new SaveReader(bytes);
            TrailingValue? value = obj.ClassName switch
            {
                "FGLightweightBuildableSubsystem" => LightweightBuildablesTrailing.Read(reader),
                _ when IsWire(obj) => PowerLineTrailing.Read(reader),
                _ => null,
            };
            if (value is null || !reader.AtEnd)
                return null;
            return value.ToBytes().AsSpan().SequenceEqual(bytes) ? value : null;
        }
        catch (SaveFormatException)
        {
            return null;
        }
    }

    /// <summary>Wires (power lines, holiday light strings) carry mWireInstances.</summary>
    private static bool IsWire(SaveObject obj) => obj.Properties.Find("mWireInstances") is not null;
}
