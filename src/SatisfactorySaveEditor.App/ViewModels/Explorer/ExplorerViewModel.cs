using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Domain.Explorer;

namespace SatisfactorySaveEditor.App.ViewModels.Explorer;

public sealed record ObjectItem(SaveObject Object, string Level)
{
    public string ClassName => Object.ClassName;
    /// <summary>Instance name without the "Persistent_Level:PersistentLevel." prefix.</summary>
    public string ShortName
    {
        get
        {
            var name = Object.InstanceName ?? "";
            int dot = name.IndexOf('.');
            return dot >= 0 ? name[(dot + 1)..] : name;
        }
    }
}

public sealed record ClassFilter(string? ClassName, int Count)
{
    public override string ToString() => ClassName is null ? $"All classes ({Count:N0})" : $"{ClassName} ({Count:N0})";
}

/// <summary>
/// Generic property explorer: a searchable object list, the selected object's property tree and an
/// editor for the selected node.
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly SaveDocument _document;
    private readonly List<ObjectItem> _allObjects;

    public ExplorerViewModel(SaveDocument document)
    {
        _document = document;
        _allObjects = document.Save.Body.Levels
            .SelectMany(l => l.Objects.Select(o => new ObjectItem(o, l.Name ?? "Persistent level")))
            .ToList();
        ClassFilters =
        [
            new ClassFilter(null, _allObjects.Count),
            .. _allObjects.GroupBy(o => o.ClassName).OrderBy(g => g.Key).Select(g => new ClassFilter(g.Key, g.Count())),
        ];
        SelectedClass = ClassFilters[0]; // triggers the initial filter
    }

    public int ObjectCount => _allObjects.Count;
    public IReadOnlyList<ClassFilter> ClassFilters { get; }
    public ObservableCollection<PropertyNodeViewModel> Nodes { get; } = [];

    /// <summary>Filtered objects; replaced wholesale on each filter change (cheaper than per-item notifications).</summary>
    [ObservableProperty] private IReadOnlyList<ObjectItem> _objects = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ClassFilter _selectedClass = null!;
    [ObservableProperty] private string _listSummary = "";
    [ObservableProperty] private ObjectItem? _selectedObject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditText), nameof(CanEditBool), nameof(HasSelectedNode))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(AddElementCommand), nameof(RemoveElementCommand))]
    private PropertyNodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _editText = "";

    [ObservableProperty] private bool _editBool;
    [ObservableProperty] private string? _editError;

    public bool HasSelectedNode => SelectedNode is not null;
    public bool CanEditText => SelectedNode?.Node.EditKind == ExplorerEditKind.Text;
    public bool CanEditBool => SelectedNode?.Node.EditKind == ExplorerEditKind.Bool;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedClassChanged(ClassFilter value) => ApplyFilter();

    partial void OnSelectedObjectChanged(ObjectItem? value)
    {
        SelectedNode = null;
        Nodes.Clear();
        if (value is null)
            return;
        foreach (var node in ExplorerTree.ForObject(value.Object))
            Nodes.Add(new PropertyNodeViewModel(node, null));
    }

    private bool _suppressBoolApply;

    partial void OnSelectedNodeChanged(PropertyNodeViewModel? value)
    {
        EditError = null;
        EditText = value?.ValueText ?? "";
        _suppressBoolApply = true;
        EditBool = value?.Node.EditKind == ExplorerEditKind.Bool && bool.TryParse(value.ValueText, out bool b) && b;
        _suppressBoolApply = false;
    }

    partial void OnEditBoolChanged(bool value)
    {
        if (!_suppressBoolApply && CanEditBool)
            Apply(value.ToString());
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => Apply(EditText);

    private bool CanApply() => CanEditText;

    private void Apply(string text)
    {
        if (SelectedNode is null)
            return;
        if (!SelectedNode.Node.TrySetValue(text, out var error))
        {
            EditError = error;
            return;
        }
        EditError = null;
        _document.MarkDirty();
        SelectedNode.RefreshValue();
        EditText = SelectedNode.ValueText;
    }

    [RelayCommand(CanExecute = nameof(CanAddElement))]
    private void AddElement()
    {
        var node = SelectedNode!;
        node.Node.AddElement();
        _document.MarkDirty();
        node.Reload();
        node.IsExpanded = true;
    }

    private bool CanAddElement() => SelectedNode?.Node.CanAddElement == true;

    [RelayCommand(CanExecute = nameof(CanRemoveElement))]
    private void RemoveElement()
    {
        var node = SelectedNode!;
        node.Node.Remove();
        _document.MarkDirty();
        SelectedNode = node.Parent;
        node.Parent?.Reload();
    }

    private bool CanRemoveElement() => SelectedNode?.Node.CanRemove == true;

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var matches = _allObjects.Where(o =>
            (SelectedClass.ClassName is null || o.ClassName == SelectedClass.ClassName) &&
            (search.Length == 0
             || (o.Object.InstanceName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
             || (o.Object.TypePath ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)));

        Objects = matches.ToList();
        ListSummary = $"{Objects.Count:N0} objects";
    }
}
