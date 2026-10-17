using System.Globalization;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Domain.Explorer;

public enum ExplorerEditKind
{
    /// <summary>Read-only node (containers, raw bytes, unsupported values).</summary>
    None,
    /// <summary>Edited as text and parsed/validated by <see cref="ExplorerNode.TrySetValue"/>.</summary>
    Text,
    /// <summary>Edited as a checkbox ("True"/"False").</summary>
    Bool,
}

/// <summary>
/// A UI-agnostic node in the generic property tree: one property, struct field, container element or
/// reference part. Scalar nodes can be edited through <see cref="TrySetValue"/>; array nodes can add and
/// remove elements. Children are built on demand so large saves stay cheap to browse.
/// </summary>
public sealed class ExplorerNode
{
    private readonly Func<string> _getValue;
    private readonly Func<string, string?>? _setValue;
    private readonly Func<IReadOnlyList<ExplorerNode>>? _buildChildren;
    private IReadOnlyList<ExplorerNode>? _children;

    internal ExplorerNode(string name, string typeName, Func<string> getValue, ExplorerEditKind editKind = ExplorerEditKind.None,
        Func<string, string?>? setValue = null, Func<IReadOnlyList<ExplorerNode>>? buildChildren = null)
    {
        Name = name;
        TypeName = typeName;
        _getValue = getValue;
        EditKind = setValue is null ? ExplorerEditKind.None : editKind;
        _setValue = setValue;
        _buildChildren = buildChildren;
    }

    public string Name { get; }
    public string TypeName { get; }
    public string ValueText => _getValue();
    public ExplorerEditKind EditKind { get; }
    public bool HasChildren => _buildChildren is not null;

    /// <summary>The array this node can append to, when it is an array node.</summary>
    internal ArrayValue? Array { get; init; }
    /// <summary>The array containing this node, when it is an array element.</summary>
    internal ArrayValue? ParentArray { get; init; }
    internal PropertyValue? Value { get; init; }

    public bool CanAddElement => Array is not null && ExplorerTree.CanCreateElement(Array);
    public bool CanRemove => ParentArray is not null && Value is not null;

    public IReadOnlyList<ExplorerNode> Children => _children ??= _buildChildren?.Invoke() ?? [];

    /// <summary>Forces children to be rebuilt (after elements are added or removed).</summary>
    public void InvalidateChildren() => _children = null;

    /// <summary>Parses and applies a new value. Returns false with a message when the text is invalid.</summary>
    public bool TrySetValue(string text, out string? error)
    {
        if (_setValue is null)
        {
            error = "This value cannot be edited.";
            return false;
        }
        error = _setValue(text);
        return error is null;
    }

    /// <summary>Appends an element (a copy of the last one, or a default value) and returns its node.</summary>
    public void AddElement()
    {
        if (Array is null || !CanAddElement)
            throw new InvalidOperationException($"{Name} does not support adding elements.");
        Array.Elements.Add(ExplorerTree.CreateElement(Array));
        InvalidateChildren();
    }

    /// <summary>Removes this element from its array. The parent node must be refreshed afterwards.</summary>
    public void Remove()
    {
        if (ParentArray is null || Value is null)
            throw new InvalidOperationException($"{Name} is not an array element.");
        int index = ParentArray.Elements.FindIndex(e => ReferenceEquals(e, Value));
        if (index < 0)
            throw new InvalidOperationException($"{Name} is no longer in its array.");
        ParentArray.Elements.RemoveAt(index);
    }

    public override string ToString() => $"{Name} = {ValueText}";
}

/// <summary>Builds explorer nodes for save objects and property values.</summary>
public static class ExplorerTree
{
    private const int MaxHexPreview = 64;

    public static IReadOnlyList<ExplorerNode> ForObject(SaveObject obj)
    {
        var nodes = new List<ExplorerNode>();
        if (obj is ActorObject actor)
        {
            nodes.Add(Group("Transform", "Actor header", () =>
            [
                Leaf("Position", "Vector (float)", () => actor.Position.ToString()),
                Leaf("Rotation", "Quat (float)", () => actor.Rotation.ToString()),
                Leaf("Scale", "Vector (float)", () => actor.Scale.ToString()),
            ]));
            if (!actor.Parent.IsEmpty)
                nodes.Add(Leaf("Parent", "ObjectReference", () => actor.Parent.ToString()));
            if (actor.Components.Count > 0)
                nodes.Add(Group("Components", $"{actor.Components.Count} references",
                    () => actor.Components.Select((c, i) => Leaf($"[{i}]", "ObjectReference", c.ToString)).ToList()));
        }

        if (!obj.IsParsed)
        {
            nodes.Add(Leaf("Data", "Unparsed", () => $"{obj.RawData.Length} bytes: {obj.ParseError}"));
            return nodes;
        }

        nodes.AddRange(ForProperties(obj.Properties));
        if (obj.TrailingData.Length > 0)
            nodes.Add(Leaf("Trailing data", $"{obj.TrailingData.Length} bytes", () => Hex(obj.TrailingData)));
        return nodes;
    }

    public static IReadOnlyList<ExplorerNode> ForProperties(PropertyList properties) =>
        properties.Select(p => ForValue(p.Tag.ArrayIndex == 0 ? p.Name ?? "?" : $"{p.Name}[{p.Tag.ArrayIndex}]",
            p.Type.ToString(), p.Value)).ToList();

    private static ExplorerNode ForValue(string name, string typeName, PropertyValue value, ArrayValue? parentArray = null)
    {
        switch (value)
        {
            case IntValue v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<int>(t, x => v.Value = x), parentArray);
            case Int8Value v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<sbyte>(t, x => v.Value = x), parentArray);
            case Int16Value v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<short>(t, x => v.Value = x), parentArray);
            case UInt16Value v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<ushort>(t, x => v.Value = x), parentArray);
            case UInt32Value v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<uint>(t, x => v.Value = x), parentArray);
            case Int64Value v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<long>(t, x => v.Value = x), parentArray);
            case UInt64Value v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<ulong>(t, x => v.Value = x), parentArray);
            case ByteValue v: return Scalar(name, typeName, v, () => v.Value.ToString(CultureInfo.InvariantCulture), t => Parse<byte>(t, x => v.Value = x), parentArray);
            case FloatValue v: return Scalar(name, typeName, v, () => v.Value.ToString("R", CultureInfo.InvariantCulture), t => ParseFloat(t, x => v.Value = (float)x, float.MaxValue), parentArray);
            case DoubleValue v: return Scalar(name, typeName, v, () => v.Value.ToString("R", CultureInfo.InvariantCulture), t => ParseFloat(t, x => v.Value = x, double.MaxValue), parentArray);
            case BoolValue v:
                return new ExplorerNode(name, typeName, () => v.Value.ToString(), ExplorerEditKind.Bool,
                    t => bool.TryParse(t, out bool b) ? Set(() => v.Value = b) : "Expected True or False.")
                { ParentArray = parentArray, Value = v };
            case StringValue v:
                return new ExplorerNode(name, typeName, () => v.Value ?? "", ExplorerEditKind.Text,
                    t => Set(() => v.Value = t.Length == 0 ? null : t))
                { ParentArray = parentArray, Value = v };
            case ObjectValue v:
                return new ExplorerNode(name, typeName, () => v.Value.ToString(), buildChildren: () =>
                [
                    TextField("Level", () => v.Value.LevelName, s => v.Value = v.Value with { LevelName = s }),
                    TextField("Path", () => v.Value.PathName, s => v.Value = v.Value with { PathName = s }),
                ])
                { ParentArray = parentArray, Value = v };
            case SoftObjectValue v:
                return new ExplorerNode(name, typeName, v.ToString, buildChildren: () =>
                [
                    TextField("Level", () => v.Value.LevelName, s => v.Value = v.Value with { LevelName = s }),
                    TextField("Path", () => v.Value.PathName, s => v.Value = v.Value with { PathName = s }),
                    TextField("SubPath", () => v.SubPath, s => v.SubPath = s),
                ])
                { ParentArray = parentArray, Value = v };
            case TextValue v:
                return new ExplorerNode(name, typeName, v.ToString, buildChildren: () => TextChildren(v))
                { ParentArray = parentArray, Value = v };
            case PropertyListStruct v:
                return new ExplorerNode(name, typeName, () => $"{v.StructName} ({v.Properties.Count} properties)",
                    buildChildren: () => ForProperties(v.Properties))
                { ParentArray = parentArray, Value = v };
            case NativeStruct v:
                return new ExplorerNode(name, typeName, v.ToString, buildChildren: () => NativeFields(v))
                { ParentArray = parentArray, Value = v };
            case InventoryItemStruct v:
                return new ExplorerNode(name, typeName, v.ToString, buildChildren: () =>
                {
                    var children = new List<ExplorerNode> { TextField("ItemClass", () => v.ItemClass, s => v.ItemClass = s) };
                    if (v.State is not null)
                        children.Add(Group("State", v.StateType ?? "", () => ForProperties(v.State)));
                    return children;
                })
                { ParentArray = parentArray, Value = v };
            case ArrayValue v:
                return new ExplorerNode(name, typeName, () => $"{v.Elements.Count} elements", buildChildren: () =>
                    v.Elements.Select((e, i) => ForValue($"[{i}]", v.ElementType.ToString(), e, v)).ToList())
                { Array = v, ParentArray = parentArray, Value = v };
            case SetValue v:
                return new ExplorerNode(name, typeName, () => $"{v.Elements.Count} elements", buildChildren: () =>
                    v.Elements.Select((e, i) => ForValue($"[{i}]", v.ElementType.ToString(), e)).ToList())
                { ParentArray = parentArray, Value = v };
            case MapValue v:
                return new ExplorerNode(name, typeName, () => $"{v.Entries.Count} entries", buildChildren: () =>
                    v.Entries.Select((e, i) => Group($"[{i}] {Short(e.Key)}", v.ValueType.ToString(), () =>
                    [
                        ForValue("Key", v.KeyType.ToString(), e.Key),
                        ForValue("Value", v.ValueType.ToString(), e.Value),
                    ])).ToList())
                { ParentArray = parentArray, Value = v };
            case RawStruct v:
                return new ExplorerNode(name, typeName, () => $"{v.StructName} raw: {Hex(v.Bytes)}") { ParentArray = parentArray, Value = v };
            case RawValue v:
                return new ExplorerNode(name, typeName, () => $"raw: {Hex(v.Bytes)}") { ParentArray = parentArray, Value = v };
            default:
                return new ExplorerNode(name, typeName, () => value.ToString() ?? "") { ParentArray = parentArray, Value = value };
        }
    }

    internal static bool CanCreateElement(ArrayValue array) =>
        array.Elements.Count > 0 || DefaultElement(array.ElementType) is not null;

    internal static PropertyValue CreateElement(ArrayValue array) =>
        array.Elements.Count > 0
            ? PropertyCloner.Clone(array.Elements[^1])
            : DefaultElement(array.ElementType) ?? throw new InvalidOperationException($"Cannot create a {array.ElementType} element.");

    private static PropertyValue? DefaultElement(PropertyTypeName type) => type.Name switch
    {
        "IntProperty" => new IntValue(0),
        "Int8Property" => new Int8Value(0),
        "Int16Property" => new Int16Value(0),
        "UInt16Property" => new UInt16Value(0),
        "UInt32Property" => new UInt32Value(0),
        "Int64Property" => new Int64Value(0),
        "UInt64Property" => new UInt64Value(0),
        "FloatProperty" => new FloatValue(0),
        "DoubleProperty" => new DoubleValue(0),
        "BoolProperty" => new BoolValue(false),
        "ByteProperty" => new ByteValue(0),
        "StrProperty" or "NameProperty" or "EnumProperty" => new StringValue(null),
        "ObjectProperty" or "InterfaceProperty" => new ObjectValue(ObjectReference.Empty),
        _ => null,
    };

    private static List<ExplorerNode> NativeFields(NativeStruct s) =>
        s.Layout.Fields.Select((field, i) =>
        {
            string typeName = field.Kind.ToString();
            Func<string> get = () => s.Values[i] switch
            {
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
                var o => o.ToString() ?? "",
            };
            Func<string, string?> set = field.Kind switch
            {
                NativeFieldKind.Byte => t => Parse<byte>(t, x => s.Values[i] = x),
                NativeFieldKind.Int32 => t => Parse<int>(t, x => s.Values[i] = x),
                NativeFieldKind.UInt32 => t => Parse<uint>(t, x => s.Values[i] = x),
                NativeFieldKind.Int64 => t => Parse<long>(t, x => s.Values[i] = x),
                NativeFieldKind.Float => t => ParseFloat(t, x => s.Values[i] = (float)x, float.MaxValue),
                NativeFieldKind.Double => t => ParseFloat(t, x => s.Values[i] = x, double.MaxValue),
                NativeFieldKind.Guid => t => Guid.TryParse(t, out var g) ? Set(() => s.Values[i] = g) : "Expected a GUID.",
                _ => _ => "Unsupported field.",
            };
            return new ExplorerNode(field.Name, typeName, get, ExplorerEditKind.Text, set);
        }).ToList();

    private static List<ExplorerNode> TextChildren(TextValue v)
    {
        var children = new List<ExplorerNode> { Leaf("History", "int8", () => v.HistoryType.ToString()) };
        switch (v.HistoryType)
        {
            case TextValue.HistoryNone:
                children.Add(TextField("Text", () => v.Text, s => { v.Text = s; v.HasCultureInvariantString = s is not null; }));
                break;
            case TextValue.HistoryBase:
                children.Add(TextField("Namespace", () => v.Namespace, s => v.Namespace = s));
                children.Add(TextField("Key", () => v.Key, s => v.Key = s));
                children.Add(TextField("Text", () => v.Text, s => v.Text = s));
                break;
            case TextValue.HistoryStringTableEntry:
                children.Add(TextField("Table", () => v.Namespace, s => v.Namespace = s));
                children.Add(TextField("Key", () => v.Key, s => v.Key = s));
                break;
        }
        return children;
    }

    private static ExplorerNode Scalar(string name, string typeName, PropertyValue value, Func<string> get, Func<string, string?> set, ArrayValue? parentArray) =>
        new(name, typeName, get, ExplorerEditKind.Text, set) { ParentArray = parentArray, Value = value };

    private static ExplorerNode TextField(string name, Func<string?> get, Action<string?> set) =>
        new(name, "String", () => get() ?? "", ExplorerEditKind.Text, t => Set(() => set(t.Length == 0 ? null : t)));

    private static ExplorerNode Leaf(string name, string typeName, Func<string> get) => new(name, typeName, get);

    private static ExplorerNode Group(string name, string typeName, Func<IReadOnlyList<ExplorerNode>> children) =>
        new(name, typeName, () => "", buildChildren: children);

    private static string? Set(Action apply)
    {
        apply();
        return null;
    }

    private static string? Parse<T>(string text, Action<T> apply) where T : IParsable<T>
    {
        if (!T.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var value))
            return $"Expected a {typeof(T).Name} value.";
        apply(value);
        return null;
    }

    private static string? ParseFloat(string text, Action<double> apply, double max)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            return "Expected a finite number.";
        if (Math.Abs(value) > max)
            return "Number is out of range.";
        apply(value);
        return null;
    }

    private static string Short(PropertyValue value)
    {
        var text = value.ToString() ?? "";
        return text.Length > 60 ? text[..57] + "..." : text;
    }

    private static string Hex(byte[] bytes) =>
        bytes.Length <= MaxHexPreview
            ? Convert.ToHexString(bytes)
            : $"{Convert.ToHexString(bytes.AsSpan(0, MaxHexPreview))}... ({bytes.Length} bytes)";
}
