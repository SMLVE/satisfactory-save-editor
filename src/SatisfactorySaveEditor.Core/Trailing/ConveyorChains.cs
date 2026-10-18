using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Trailing;

/// <summary>A spline control point (location and tangents) of a belt segment, relative to the belt.</summary>
public readonly record struct SplinePoint(DVector3 Location, DVector3 ArriveTangent, DVector3 LeaveTangent)
{
    internal static SplinePoint Read(SaveReader r) => new(DVector3.Read(r), DVector3.Read(r), DVector3.Read(r));

    internal void Write(SaveWriter w)
    {
        Location.Write(w);
        ArriveTangent.Write(w);
        LeaveTangent.Write(w);
    }
}

/// <summary>One belt's part of a conveyor chain: its spline and the range of chain items on it.</summary>
public sealed class ConveyorChainSegment
{
    public ObjectReference Chain { get; set; } = ObjectReference.Empty;
    public ObjectReference Belt { get; set; } = ObjectReference.Empty;
    public List<SplinePoint> SplinePoints { get; } = [];
    public float OffsetAtStart { get; set; }
    /// <summary>Distance along the chain where this belt starts (cm).</summary>
    public float StartsAtLength { get; set; }
    /// <summary>Distance along the chain where this belt ends (cm).</summary>
    public float EndsAtLength { get; set; }
    public int FirstItemIndex { get; set; }
    public int LastItemIndex { get; set; }
    public int IndexInChainArray { get; set; }

    internal static ConveyorChainSegment Read(SaveReader r)
    {
        var segment = new ConveyorChainSegment { Chain = ObjectReference.Read(r), Belt = ObjectReference.Read(r) };
        int points = r.ReadInt32();
        for (int i = 0; i < points; i++)
            segment.SplinePoints.Add(SplinePoint.Read(r));
        segment.OffsetAtStart = r.ReadSingle();
        segment.StartsAtLength = r.ReadSingle();
        segment.EndsAtLength = r.ReadSingle();
        segment.FirstItemIndex = r.ReadInt32();
        segment.LastItemIndex = r.ReadInt32();
        segment.IndexInChainArray = r.ReadInt32();
        return segment;
    }

    internal void Write(SaveWriter w)
    {
        Chain.Write(w);
        Belt.Write(w);
        w.WriteInt32(SplinePoints.Count);
        foreach (var point in SplinePoints)
            point.Write(w);
        w.WriteSingle(OffsetAtStart);
        w.WriteSingle(StartsAtLength);
        w.WriteSingle(EndsAtLength);
        w.WriteInt32(FirstItemIndex);
        w.WriteInt32(LastItemIndex);
        w.WriteInt32(IndexInChainArray);
    }
}

/// <summary>An item travelling on a conveyor chain, at a distance along the chain.</summary>
public sealed class ConveyorChainItem
{
    public ObjectReference ItemClass { get; set; } = ObjectReference.Empty;
    /// <summary>Item state field; observed as 0 (no state). Non-zero values are rejected because more data may follow.</summary>
    public int State { get; set; }
    /// <summary>Distance along the chain (cm).</summary>
    public float Offset { get; set; }

    internal static ConveyorChainItem Read(SaveReader r)
    {
        var item = new ConveyorChainItem { ItemClass = ObjectReference.Read(r), State = r.ReadInt32(), Offset = r.ReadSingle() };
        if (item.State != 0)
            throw new SaveFormatException("Conveyor item with state is not decoded yet.");
        return item;
    }

    internal void Write(SaveWriter w)
    {
        ItemClass.Write(w);
        w.WriteInt32(State);
        w.WriteSingle(Offset);
    }
}

/// <summary>Trailing data of a conveyor belt or lift: items stored on the belt itself (usually none).</summary>
public sealed class ConveyorItemsTrailing : TrailingValue
{
    public List<ConveyorChainItem> Items { get; } = [];

    internal static ConveyorItemsTrailing Read(SaveReader r)
    {
        var belt = new ConveyorItemsTrailing();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            belt.Items.Add(ConveyorChainItem.Read(r));
        return belt;
    }

    internal override void Write(SaveWriter w)
    {
        w.WriteInt32(Items.Count);
        foreach (var item in Items)
            item.Write(w);
    }

    public override string ToString() => $"{Items.Count} items";
}

/// <summary>
/// Trailing data of FGConveyorChainActor: a run of connected belts treated as one item track, with the
/// belts (as segments with their splines) and the items currently on it.
/// </summary>
public sealed class ConveyorChainTrailing : TrailingValue
{
    public ObjectReference FirstBelt { get; set; } = ObjectReference.Empty;
    public ObjectReference LastBelt { get; set; } = ObjectReference.Empty;
    public List<ConveyorChainSegment> Segments { get; } = [];
    public float TotalLength { get; set; }
    public int NumItems { get; set; }
    public int LeadItemIndex { get; set; }
    public int TailItemIndex { get; set; }
    public List<ConveyorChainItem> Items { get; } = [];

    internal static ConveyorChainTrailing Read(SaveReader r)
    {
        var chain = new ConveyorChainTrailing { FirstBelt = ObjectReference.Read(r), LastBelt = ObjectReference.Read(r) };
        int segments = r.ReadInt32();
        for (int i = 0; i < segments; i++)
            chain.Segments.Add(ConveyorChainSegment.Read(r));
        chain.TotalLength = r.ReadSingle();
        chain.NumItems = r.ReadInt32();
        chain.LeadItemIndex = r.ReadInt32();
        chain.TailItemIndex = r.ReadInt32();
        int items = r.ReadInt32();
        for (int i = 0; i < items; i++)
            chain.Items.Add(ConveyorChainItem.Read(r));
        return chain;
    }

    internal override void Write(SaveWriter w)
    {
        FirstBelt.Write(w);
        LastBelt.Write(w);
        w.WriteInt32(Segments.Count);
        foreach (var segment in Segments)
            segment.Write(w);
        w.WriteSingle(TotalLength);
        w.WriteInt32(NumItems);
        w.WriteInt32(LeadItemIndex);
        w.WriteInt32(TailItemIndex);
        w.WriteInt32(Items.Count);
        foreach (var item in Items)
            item.Write(w);
    }

    public override string ToString() => $"{Segments.Count} belts, {Items.Count} items, {TotalLength / 100:0.#} m";
}
