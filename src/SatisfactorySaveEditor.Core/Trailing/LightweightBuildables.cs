using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Trailing;

/// <summary>Double-precision vector as stored in lightweight buildable transforms.</summary>
public readonly record struct DVector3(double X, double Y, double Z)
{
    internal static DVector3 Read(SaveReader r) => new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
    internal void Write(SaveWriter w) { w.WriteDouble(X); w.WriteDouble(Y); w.WriteDouble(Z); }
}

/// <summary>Double-precision quaternion as stored in lightweight buildable transforms.</summary>
public readonly record struct DQuat(double X, double Y, double Z, double W)
{
    internal static DQuat Read(SaveReader r) => new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
    internal void Write(SaveWriter w) { w.WriteDouble(X); w.WriteDouble(Y); w.WriteDouble(Z); w.WriteDouble(W); }

    /// <summary>Rotation around the vertical axis in radians.</summary>
    public double Yaw => Math.Atan2(2 * (W * Z + X * Y), 1 - 2 * (Y * Y + Z * Z));
}

/// <summary>RGBA color with float components.</summary>
public readonly record struct LinearColor(float R, float G, float B, float A)
{
    internal static LinearColor Read(SaveReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    internal void Write(SaveWriter w) { w.WriteSingle(R); w.WriteSingle(G); w.WriteSingle(B); w.WriteSingle(A); }
}

/// <summary>
/// One foundation, wall, ramp, walkway... stored in the lightweight buildable subsystem instead of as an actor.
/// Layout (save version 60, subsystem version 4): transform, customization, recipe, blueprint proxy and a
/// small fixed tail.
/// </summary>
public sealed class LightweightInstance
{
    public DQuat Rotation { get; set; }
    public DVector3 Position { get; set; }
    public DVector3 Scale { get; set; }

    public ObjectReference Swatch { get; set; } = ObjectReference.Empty;
    public ObjectReference Material { get; set; } = ObjectReference.Empty;
    public ObjectReference Pattern { get; set; } = ObjectReference.Empty;
    public ObjectReference Skin { get; set; } = ObjectReference.Empty;
    public LinearColor PrimaryColor { get; set; }
    public LinearColor SecondaryColor { get; set; }
    public ObjectReference PaintFinish { get; set; } = ObjectReference.Empty;
    public byte PatternRotation { get; set; }

    public ObjectReference Recipe { get; set; } = ObjectReference.Empty;
    public ObjectReference BlueprintProxy { get; set; } = ObjectReference.Empty;

    /// <summary>Tail fields observed as 0, 0 and -1. Other values may announce extra data, so they are rejected.</summary>
    public int TailValue0 { get; set; }
    public byte TailValue1 { get; set; }
    public int TailValue2 { get; set; } = -1;

    internal static LightweightInstance Read(SaveReader r)
    {
        var i = new LightweightInstance
        {
            Rotation = DQuat.Read(r),
            Position = DVector3.Read(r),
            Scale = DVector3.Read(r),
            Swatch = ObjectReference.Read(r),
            Material = ObjectReference.Read(r),
            Pattern = ObjectReference.Read(r),
            Skin = ObjectReference.Read(r),
            PrimaryColor = LinearColor.Read(r),
            SecondaryColor = LinearColor.Read(r),
            PaintFinish = ObjectReference.Read(r),
            PatternRotation = r.ReadByte(),
            Recipe = ObjectReference.Read(r),
            BlueprintProxy = ObjectReference.Read(r),
            TailValue0 = r.ReadInt32(),
            TailValue1 = r.ReadByte(),
            TailValue2 = r.ReadInt32(),
        };
        if (i.TailValue0 != 0 || i.TailValue1 != 0)
            throw new SaveFormatException("Lightweight buildable instance carries type-specific data, which is not decoded yet.");
        return i;
    }

    internal void Write(SaveWriter w)
    {
        Rotation.Write(w);
        Position.Write(w);
        Scale.Write(w);
        Swatch.Write(w);
        Material.Write(w);
        Pattern.Write(w);
        Skin.Write(w);
        PrimaryColor.Write(w);
        SecondaryColor.Write(w);
        PaintFinish.Write(w);
        w.WriteByte(PatternRotation);
        Recipe.Write(w);
        BlueprintProxy.Write(w);
        w.WriteInt32(TailValue0);
        w.WriteByte(TailValue1);
        w.WriteInt32(TailValue2);
    }
}

/// <summary>All instances of one buildable class, e.g. Build_Foundation_8x4_01_C.</summary>
public sealed class LightweightClass
{
    public ObjectReference BuildableClass { get; set; } = ObjectReference.Empty;
    public List<LightweightInstance> Instances { get; } = [];

    public string ClassName
    {
        get
        {
            var path = BuildableClass.PathName ?? "";
            return path[(path.LastIndexOf('.') + 1)..];
        }
    }
}

/// <summary>Trailing data of FGLightweightBuildableSubsystem: every lightweight buildable grouped by class.</summary>
public sealed class LightweightBuildablesTrailing : TrailingValue
{
    public const int SupportedVersion = 4;

    public int Version { get; set; } = SupportedVersion;
    public List<LightweightClass> Classes { get; } = [];

    public int InstanceCount => Classes.Sum(c => c.Instances.Count);

    internal static LightweightBuildablesTrailing Read(SaveReader r)
    {
        var data = new LightweightBuildablesTrailing { Version = r.ReadInt32() };
        if (data.Version != SupportedVersion)
            throw new SaveFormatException($"Unsupported lightweight buildable data version {data.Version}.");
        int classCount = r.ReadInt32();
        for (int c = 0; c < classCount; c++)
        {
            var cls = new LightweightClass { BuildableClass = ObjectReference.Read(r) };
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
                cls.Instances.Add(LightweightInstance.Read(r));
            data.Classes.Add(cls);
        }
        return data;
    }

    internal override void Write(SaveWriter w)
    {
        w.WriteInt32(Version);
        w.WriteInt32(Classes.Count);
        foreach (var cls in Classes)
        {
            cls.BuildableClass.Write(w);
            w.WriteInt32(cls.Instances.Count);
            foreach (var instance in cls.Instances)
                instance.Write(w);
        }
    }

    public override string ToString() => $"{InstanceCount} lightweight buildables in {Classes.Count} classes";
}
