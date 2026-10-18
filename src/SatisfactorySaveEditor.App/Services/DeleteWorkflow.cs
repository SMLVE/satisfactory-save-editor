using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Deletion;
using SatisfactorySaveEditor.Domain.Inventory;
using SatisfactorySaveEditor.Domain.Structures;

namespace SatisfactorySaveEditor.App.Services;

/// <summary>Plans a deletion, shows what it will do (or why it is not possible), and applies it on confirmation.</summary>
public static class DeleteWorkflow
{
    /// <summary>Returns true when the object was deleted.</summary>
    public static async Task<bool> RunAsync(SaveDocument document, IDialogService dialogs, ActorObject actor)
    {
        var deletion = document.Editor.Deletion;
        var plan = deletion.Plan(actor);
        string name = $"{ContainerService.FriendlyClassName(actor.ClassName)} ({ShortName(actor)})";
        if (!plan.CanDelete)
        {
            await dialogs.ShowMessageAsync("Can't delete", $"{name} can't be deleted. {plan.BlockedReason}");
            return false;
        }

        var details = new List<string> { $"Remove {name} and its {plan.ComponentCount} components" };
        details.AddRange(plan.AlsoDeleted.Select(a => a.Trailing switch
        {
            PowerLineTrailing => $"Also remove the power line {document.Editor.Power.Resolve(a).Description} ({ShortName(a)})",
            ConveyorChainTrailing chain =>
                $"Also remove its conveyor chain ({chain.Segments.Count} belts, {chain.Items.Count} items on them are lost). " +
                "The game rebuilds chains for the remaining belts when the save loads.",
            _ => $"Also remove {ContainerService.FriendlyClassName(a.ClassName)} ({ShortName(a)}), which is attached to it",
        }));
        details.AddRange(plan.ReferencesToClear.Select(r => $"Unlink {r.Description}"));
        var held = plan.ObjectsToRemove.Where(o => o.IsParsed && o.Properties.Find("mInventoryStacks") is not null)
            .Select(o => document.Editor.OpenInventory(o).Slots.Count(s => !s.IsEmpty)).Sum();
        if (held > 0)
            details.Add($"The {held} item stacks it holds are lost");
        int onBelt = plan.ObjectsToRemove.Select(o => o.Trailing).OfType<ConveyorItemsTrailing>().Sum(t => t.Items.Count);
        if (onBelt > 0)
            details.Add($"The {onBelt} items stored on the belt are lost");

        if (!await dialogs.ConfirmListAsync("Delete", $"Delete {name}? This is applied when you save.", details, "Delete"))
            return false;

        deletion.Execute(plan);
        var extras = plan.AlsoDeleted.Count() is var n and > 0 ? $", {n} attached object(s)" : "";
        document.RecordChange($"Deleted {name} (+{plan.ComponentCount} components{extras}, {plan.ReferencesToClear.Count} references unlinked)");
        document.NotifyStructureChanged();
        return true;
    }

    /// <summary>
    /// Deletes a box selection: lightweight structures plus actors (through the deletion planner). Shows one
    /// summarized preview and records one change. Returns true when something was deleted.
    /// </summary>
    public static async Task<bool> RunSelectionAsync(SaveDocument document, IDialogService dialogs,
        IReadOnlyList<ActorObject> actors, IReadOnlyList<Structure> structures)
    {
        var editor = document.Editor;
        var details = new List<string>();
        foreach (var g in structures.GroupBy(s => s.TypeName).OrderByDescending(g => g.Count()))
            details.Add($"Remove {g.Count()} × {g.Key}");

        DeletionPlan? plan = null;
        if (actors.Count > 0)
        {
            plan = editor.Deletion.Plan(actors);
            if (plan.CanDelete)
            {
                foreach (var g in plan.Targets.Except(plan.Skipped.Select(s => s.Actor))
                             .GroupBy(a => ContainerService.FriendlyClassName(a.ClassName)).OrderByDescending(g => g.Count()))
                    details.Add($"Remove {g.Count()} × {g.Key}");
                details.Add($"…with their {plan.ComponentCount} components");
                foreach (var g in plan.AlsoDeleted.GroupBy(a => a.Trailing switch
                         {
                             PowerLineTrailing => "attached power lines",
                             ConveyorChainTrailing => "conveyor chains (rebuilt by the game on load; items on them are lost)",
                             _ => $"attached {ContainerService.FriendlyClassName(a.ClassName)}",
                         }))
                    details.Add($"Also remove {g.Count()} {g.Key}");
                if (plan.ReferencesToClear.Count > 0)
                    details.Add($"Unlink {plan.ReferencesToClear.Count} references from objects that stay (belt ends, power grids, wires)");
            }
            foreach (var g in plan.Skipped.GroupBy(s => s.Reason))
                details.Add($"Skip {g.Count()} object(s): {g.Key}");
        }

        bool deletesActors = plan is { CanDelete: true };
        if (structures.Count == 0 && !deletesActors)
        {
            await dialogs.ShowMessageAsync("Can't delete", plan?.BlockedReason ?? "Nothing is selected.");
            return false;
        }
        int total = structures.Count + (deletesActors ? plan!.Targets.Count - plan.Skipped.Count : 0);
        if (!await dialogs.ConfirmListAsync("Delete selection", $"Delete {total} selected objects? This is applied when you save.", details, "Delete"))
            return false;

        if (deletesActors)
            editor.Deletion.Execute(plan!);
        int removedStructures = structures.Count > 0 ? editor.Structures.Delete(structures) : 0;
        document.RecordChange($"Deleted a selection: {removedStructures} foundations/walls, " +
                              $"{(deletesActors ? plan!.Targets.Count - plan.Skipped.Count : 0)} buildings");
        document.NotifyStructureChanged();
        return true;
    }

    private static string ShortName(SaveObject obj) => obj.InstanceName?[(obj.InstanceName.LastIndexOf('.') + 1)..] ?? "";
}
