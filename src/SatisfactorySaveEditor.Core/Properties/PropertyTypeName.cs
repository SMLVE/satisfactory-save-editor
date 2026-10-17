using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Properties;

/// <summary>
/// A property type with its parameters, e.g. <c>ArrayProperty&lt;StructProperty&lt;Vector&lt;/Script/CoreUObject&gt;&gt;&gt;</c>.
/// Modern tags (UE 5.4+) serialize the whole tree; legacy tags carry the parameters in type-specific header fields.
/// </summary>
public sealed class PropertyTypeName
{
    public PropertyTypeName(string? name, params PropertyTypeName[] parameters)
    {
        Name = name;
        Parameters = [.. parameters];
    }

    public string? Name { get; set; }
    public List<PropertyTypeName> Parameters { get; }

    public PropertyTypeName? Param(int index) => index < Parameters.Count ? Parameters[index] : null;

    public static PropertyTypeName Read(SaveReader r)
    {
        var name = r.ReadFString();
        int count = r.ReadInt32();
        var type = new PropertyTypeName(name);
        for (int i = 0; i < count; i++)
            type.Parameters.Add(Read(r));
        return type;
    }

    public void Write(SaveWriter w)
    {
        w.WriteFString(Name);
        w.WriteInt32(Parameters.Count);
        foreach (var p in Parameters)
            p.Write(w);
    }

    public override string ToString() =>
        Parameters.Count == 0 ? Name ?? "" : $"{Name}<{string.Join(", ", Parameters)}>";
}
