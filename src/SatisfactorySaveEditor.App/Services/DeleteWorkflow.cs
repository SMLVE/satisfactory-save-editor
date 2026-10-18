using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Domain.Inventory;

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
        details.AddRange(plan.AlsoDeleted.Select(a => $"Also remove {ContainerService.FriendlyClassName(a.ClassName)} ({ShortName(a)}), which is attached to it"));
        details.AddRange(plan.ReferencesToClear.Select(r => $"Unlink {r.Description}"));
        var held = plan.ObjectsToRemove.Where(o => o.IsParsed && o.Properties.Find("mInventoryStacks") is not null)
            .Select(o => document.Editor.OpenInventory(o).Slots.Count(s => !s.IsEmpty)).Sum();
        if (held > 0)
            details.Add($"The {held} item stacks it holds are lost");

        if (!await dialogs.ConfirmListAsync("Delete", $"Delete {name}? This is applied when you save.", details, "Delete"))
            return false;

        deletion.Execute(plan);
        var extras = plan.AlsoDeleted.Count() is var n and > 0 ? $", {n} attached object(s)" : "";
        document.RecordChange($"Deleted {name} (+{plan.ComponentCount} components{extras}, {plan.ReferencesToClear.Count} references unlinked)");
        document.NotifyStructureChanged();
        return true;
    }

    private static string ShortName(SaveObject obj) => obj.InstanceName?[(obj.InstanceName.LastIndexOf('.') + 1)..] ?? "";
}
