using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Domain.Explorer;

namespace SatisfactorySaveEditor.Domain.Tests;

public class ExplorerTests
{
    private const string Inventory = "Persistent_Level:PersistentLevel.Char_Player_C_0.inventory";

    [Fact]
    public void ObjectNodes_IncludeTransformForActors()
    {
        var editor = TestEditors.Open("sample2.sav");
        var nodes = ExplorerTree.ForObject(editor.Find("Persistent_Level:PersistentLevel.Char_Player_C_0")!);

        Assert.Equal("Transform", nodes[0].Name);
        Assert.Contains(nodes, n => n.Name == "mInventory" && n.ValueText.EndsWith("Char_Player_C_0.inventory"));
    }

    [Fact]
    public void ScalarEdit_Persists()
    {
        var editor = TestEditors.Open("sample2.sav");
        var numItems = Path(ExplorerTree.ForObject(editor.Find(Inventory)!), "mInventoryStacks", "[0]", "NumItems");
        Assert.Equal("10", numItems.ValueText);

        Assert.True(numItems.TrySetValue("123", out var error), error);

        var reloaded = TestEditors.Reload(editor);
        Assert.Equal("123", Path(ExplorerTree.ForObject(reloaded.Find(Inventory)!), "mInventoryStacks", "[0]", "NumItems").ValueText);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("99999999999")]
    [InlineData("1.5")]
    public void ScalarEdit_RejectsInvalidText(string text)
    {
        var editor = TestEditors.Open("sample2.sav");
        var numItems = Path(ExplorerTree.ForObject(editor.Find(Inventory)!), "mInventoryStacks", "[0]", "NumItems");

        Assert.False(numItems.TrySetValue(text, out var error));
        Assert.NotNull(error);
        Assert.Equal("10", numItems.ValueText);
    }

    [Fact]
    public void FloatEdit_RejectsNonFiniteValues()
    {
        var editor = TestEditors.Open("sample2.sav");
        var health = Path(ExplorerTree.ForObject(editor.Find("Persistent_Level:PersistentLevel.Char_Player_C_0.HealthComponent")!), "mCurrentHealth");

        Assert.False(health.TrySetValue("NaN", out _));
        Assert.False(health.TrySetValue("1e60", out _));
        Assert.True(health.TrySetValue("75.5", out _));
        Assert.Equal("75.5", health.ValueText);
    }

    [Fact]
    public void NativeStructField_AndInventoryItemClass_AreEditable()
    {
        var editor = TestEditors.Open("sample2.sav");
        var player = ExplorerTree.ForObject(editor.Find("Persistent_Level:PersistentLevel.Char_Player_C_0")!);
        var red = Path(player, "mCachedPlayerCustomizationData", "PrimaryColor", "R");
        var itemClass = Path(ExplorerTree.ForObject(editor.Find(Inventory)!), "mInventoryStacks", "[0]", "Item", "ItemClass");

        Assert.True(red.TrySetValue("0.25", out _));
        Assert.True(itemClass.TrySetValue("/Game/FactoryGame/Resource/Parts/Motor/Desc_Motor.Desc_Motor_C", out _));

        var reloaded = TestEditors.Reload(editor);
        Assert.Equal("0.25", Path(ExplorerTree.ForObject(reloaded.Find("Persistent_Level:PersistentLevel.Char_Player_C_0")!),
            "mCachedPlayerCustomizationData", "PrimaryColor", "R").ValueText);
        Assert.Equal("Desc_Motor_C", GameData.GameData.ClassNameOf(reloaded.Players.All[0].Inventory.GetSlot(0).ItemClass!));
    }

    [Fact]
    public void ObjectReferencePath_IsEditable()
    {
        var editor = TestEditors.Open("sample.sav");
        var manager = editor.Single("BP_SchematicManager_C");
        var first = Path(ExplorerTree.ForObject(manager), "mPurchasedSchematics", "[0]", "Path");

        Assert.True(first.TrySetValue("/Game/FactoryGame/Schematics/Progression/Schematic_3-4.Schematic_3-4_C", out _));

        Assert.True(TestEditors.Reload(editor).Progression.IsPurchased("Schematic_3-4_C"));
    }

    [Fact]
    public void ArrayElements_CanBeAddedAndRemoved()
    {
        var editor = TestEditors.Open("sample.sav");
        var manager = editor.Single("BP_SchematicManager_C");
        var array = Path(ExplorerTree.ForObject(manager), "mPurchasedSchematics");
        Assert.Equal(7, array.Children.Count);

        Assert.True(array.CanAddElement);
        array.AddElement();
        Assert.Equal(8, array.Children.Count);
        Assert.True(array.Children[7].CanRemove);
        array.Children[0].Remove();
        array.InvalidateChildren();
        Assert.Equal(7, array.Children.Count);

        var reloaded = TestEditors.Reload(editor);
        var purchased = reloaded.Progression.PurchasedSchematics;
        Assert.Equal(7, purchased.Count);
        Assert.Equal(purchased[^1], purchased[^2]); // the added element copied the last one
    }

    [Fact]
    public void EmptyScalarArray_GetsDefaultElement()
    {
        var array = new ArrayValue(PropertyTypes.Int);
        var list = new PropertyList { Property.Create("Numbers", PropertyTypes.ArrayOf(PropertyTypes.Int), array) };
        var node = ExplorerTree.ForProperties(list).Single();

        node.AddElement();

        Assert.Equal("0", node.Children.Single().ValueText);
    }

    [Fact]
    public void BoolNode_UsesCheckboxEditing()
    {
        var editor = TestEditors.Open("sample2.sav");
        var map = Path(ExplorerTree.ForObject(editor.Single("BP_UnlockSubsystem_C")), "mIsMapUnlocked");

        Assert.Equal(ExplorerEditKind.Bool, map.EditKind);
        Assert.True(map.TrySetValue("False", out _));

        var reloaded = TestEditors.Reload(editor);
        Assert.False(reloaded.Single("BP_UnlockSubsystem_C").Properties.Get<BoolValue>("mIsMapUnlocked")!.Value);
    }

    [Fact]
    public void TypeNames_AreShortWithFullFormAvailable()
    {
        var editor = TestEditors.Open("sample2.sav");
        var nodes = ExplorerTree.ForObject(editor.Find(Inventory)!);

        var stacks = Path(nodes, "mInventoryStacks");
        Assert.Equal("InventoryStack[]", stacks.TypeName);
        Assert.Equal("ArrayProperty<StructProperty<InventoryStack</Script/FactoryGame>>>", stacks.FullTypeName);
        Assert.Equal("int", Path(nodes, "mInventoryStacks", "[0]", "NumItems").TypeName);
        Assert.Equal("InventoryItem", Path(nodes, "mInventoryStacks", "[0]", "Item").TypeName);
        Assert.Equal("object[]", Path(nodes, "mAllowedItemDescriptors").TypeName);
    }

    [Theory]
    [InlineData("MapProperty", "Map<object, int>")]
    [InlineData("SetProperty", "Set<uint32>")]
    [InlineData("ByteProperty", "EGamePhase")]
    public void ShortTypeName_FormatsContainersAndEnums(string kind, string expected)
    {
        var type = kind switch
        {
            "MapProperty" => new PropertyTypeName("MapProperty", new("ObjectProperty"), new("IntProperty")),
            "SetProperty" => new PropertyTypeName("SetProperty", new PropertyTypeName("UInt32Property")),
            _ => new PropertyTypeName("ByteProperty", new PropertyTypeName("EGamePhase", new PropertyTypeName("/Script/FactoryGame"))),
        };
        Assert.Equal(expected, ExplorerTree.ShortTypeName(type));
    }

    [Fact]
    public void ItemReferences_GetDisplayNameHints()
    {
        var editor = TestEditors.Open("sample2.sav");
        var nodes = ExplorerTree.ForObject(editor.Find(Inventory)!, TestEditors.GameData);

        Assert.Equal("Iron Plate", Path(nodes, "mInventoryStacks", "[0]", "Item").Hint);
        Assert.Equal("Iron Plate", Path(nodes, "mInventoryStacks", "[0]", "Item", "ItemClass").Hint);
        Assert.Null(Path(nodes, "mInventoryStacks", "[2]", "Item").Hint); // empty slot
        Assert.Null(Path(ExplorerTree.ForObject(editor.Find(Inventory)!), "mInventoryStacks", "[0]", "Item").Hint); // no game data
    }

    [Fact]
    public void RecipeReferences_GetDisplayNameHints()
    {
        var editor = TestEditors.Open("sample.sav");
        var recipes = Path(ExplorerTree.ForObject(editor.Single("FGRecipeManager"), TestEditors.GameData), "mAvailableRecipes");

        Assert.Contains(recipes.Children, n => n.Hint == "Iron Plate" && n.ValueText.EndsWith("Recipe_IronPlate_C"));
    }

    private static ExplorerNode Path(IReadOnlyList<ExplorerNode> nodes, params string[] names)
    {
        ExplorerNode? node = null;
        foreach (var name in names)
        {
            node = (node?.Children ?? nodes).FirstOrDefault(n => n.Name == name)
                   ?? throw new Xunit.Sdk.XunitException($"No node '{name}' among: {string.Join(", ", (node?.Children ?? nodes).Select(n => n.Name))}");
        }
        return node!;
    }
}
