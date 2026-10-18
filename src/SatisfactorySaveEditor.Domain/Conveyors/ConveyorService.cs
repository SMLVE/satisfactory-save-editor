using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Core.Trailing;

namespace SatisfactorySaveEditor.Domain.Conveyors;

/// <summary>Items on a belt, grouped by item class.</summary>
public sealed record BeltItemCount(string ItemClass, int Count);

/// <summary>Conveyor chains and the items on belts.</summary>
public sealed class ConveyorService(SaveEditor editor)
{
    /// <summary>The chain actor a belt belongs to, if any.</summary>
    public ActorObject? ChainOf(SaveObject belt) =>
        editor.Resolve(belt.Properties.Get<ObjectValue>("mConveyorChainActor")?.Value) as ActorObject;

    /// <summary>
    /// Items currently on a belt: those of its chain segment (by the segment's item index range) plus any
    /// stored on the belt itself.
    /// </summary>
    public IReadOnlyList<BeltItemCount> ContentsOf(SaveObject belt)
    {
        var classes = new List<string>();
        if (ChainOf(belt)?.Trailing is ConveyorChainTrailing chain
            && chain.Segments.FirstOrDefault(s => s.Belt.PathName == belt.InstanceName) is { } segment)
        {
            for (int i = segment.FirstItemIndex; i <= segment.LastItemIndex && i < chain.Items.Count; i++)
                if (i >= 0 && chain.Items[i].ItemClass.PathName is { } path)
                    classes.Add(path);
        }
        if (belt.Trailing is ConveyorItemsTrailing own)
            classes.AddRange(own.Items.Select(i => i.ItemClass.PathName).OfType<string>());

        return classes.GroupBy(c => c).Select(g => new BeltItemCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ToList();
    }
}
