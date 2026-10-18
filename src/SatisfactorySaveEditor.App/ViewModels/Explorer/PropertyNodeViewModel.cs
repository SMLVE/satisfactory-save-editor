using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SatisfactorySaveEditor.Domain.Explorer;

namespace SatisfactorySaveEditor.App.ViewModels.Explorer;

/// <summary>Tree item wrapping an <see cref="ExplorerNode"/>; children are created when first expanded.</summary>
internal sealed partial class PropertyNodeViewModel : ObservableObject
{
    private static readonly PropertyNodeViewModel Placeholder = new();
    private bool _childrenLoaded;

    private PropertyNodeViewModel()
    {
        Node = null!;
        Children = [];
    }

    public PropertyNodeViewModel(ExplorerNode node, PropertyNodeViewModel? parent)
    {
        Node = node;
        Parent = parent;
        Children = node.HasChildren ? [Placeholder] : [];
    }

    public ExplorerNode Node { get; }
    public PropertyNodeViewModel? Parent { get; }
    public ObservableCollection<PropertyNodeViewModel> Children { get; }

    public string Name => Node?.Name ?? "…";
    /// <summary>Path from the object's root, e.g. "mInventoryStacks › [0] › NumItems".</summary>
    public string Path => Parent is null ? Name : $"{Parent.Path} › {Name}";
    public string TypeName => Node?.TypeName ?? "";
    public string FullTypeName => Node?.FullTypeName ?? "";
    public string ValueText => Node?.ValueText ?? "";
    public string? Hint => Node?.Hint;

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded)
            LoadChildren();
    }

    public void LoadChildren()
    {
        _childrenLoaded = true;
        Children.Clear();
        foreach (var child in Node.Children)
            Children.Add(new PropertyNodeViewModel(child, this));
    }

    /// <summary>Rebuilds children after the node's structure changed (elements added/removed).</summary>
    public void Reload()
    {
        Node.InvalidateChildren();
        if (_childrenLoaded || IsExpanded)
            LoadChildren();
        RefreshValue();
    }

    /// <summary>Refreshes this node's value text and its ancestors' summaries.</summary>
    public void RefreshValue()
    {
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(Hint));
        Parent?.RefreshValue();
    }
}
