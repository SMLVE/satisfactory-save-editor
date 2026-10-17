using System.Buffers.Binary;
using System.Numerics;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Domain.Players;

/// <summary>Online platform identity stored in a player state's trailing data.</summary>
public sealed record PlayerIdentity(byte Platform, byte[] Data)
{
    public string PlatformName => Platform switch { 1 => "Epic", 6 => "Steam", _ => $"Platform {Platform}" };

    /// <summary>Steam ids are a little-endian SteamID64; other platforms are shown as hex.</summary>
    public string Id => Platform == 6 && Data.Length == 8
        ? BinaryPrimitives.ReadUInt64LittleEndian(Data).ToString()
        : Convert.ToHexString(Data);

    public override string ToString() => $"{PlatformName} {Id}";
}

/// <summary>A player: the BP_PlayerState object and the Char_Player pawn it owns.</summary>
public sealed class Player
{
    public const float MaxHealth = 100f;
    private const byte IdentityFormat = 0xF1;

    private static readonly (string Slot, string Property)[] EquipmentSlotProperties =
    [
        ("Hands", "mArmsEquipmentSlot"), ("Back", "mBackEquipmentSlot"), ("Legs", "mLegsEquipmentSlot"),
        ("Head", "mHeadEquipmentSlot"), ("Body", "mBodyEquipmentSlot"),
    ];

    private readonly SaveEditor _editor;

    internal Player(SaveEditor editor, SaveObject state)
    {
        _editor = editor;
        State = state;
        Pawn = editor.Resolve(state.Properties.Get<ObjectValue>("mOwnedPawn")?.Value) as ActorObject;
        Identity = ReadIdentity(state.TrailingData);
    }

    public SaveObject State { get; }
    /// <summary>The player's character; null if the player has no pawn in this save.</summary>
    public ActorObject? Pawn { get; }
    public PlayerIdentity? Identity { get; }

    public string Name => Identity is null ? State.InstanceName ?? "Player" : $"{Identity.PlatformName} player {Identity.Id}";

    public Vector3 Position
    {
        get => RequirePawn().Position;
        set => RequirePawn().Position = value;
    }

    /// <summary>Current health. The save omits the value at full health, which reads as <see cref="MaxHealth"/>.</summary>
    public float Health
    {
        get => HealthComponent()?.Properties.Get<FloatValue>("mCurrentHealth")?.Value ?? MaxHealth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxHealth);
            var component = HealthComponent() ?? throw new InvalidOperationException($"{Name} has no health component.");
            SaveEditor.Editable(component).Properties
                .GetOrAdd("mCurrentHealth", PropertyTypes.Float, () => new FloatValue(MaxHealth)).Value = value;
        }
    }

    public Inventory.Inventory Inventory => OpenPawnInventory("mInventory")
                                            ?? throw new InvalidOperationException($"{Name} has no inventory.");

    /// <summary>Equipment slot inventories keyed by slot name (Hands, Back, Legs, Head, Body) where slot data is stored.</summary>
    public IReadOnlyDictionary<string, Inventory.Inventory> EquipmentSlots =>
        EquipmentSlotProperties
            .Select(s => (s.Slot, Inventory: OpenPawnInventory(s.Property)))
            .Where(s => s.Inventory is not null)
            .ToDictionary(s => s.Slot, s => s.Inventory!);

    private Inventory.Inventory? OpenPawnInventory(string property)
    {
        var component = _editor.Resolve(RequirePawn().Properties.Get<ObjectValue>(property)?.Value);
        return component is null ? null : _editor.TryOpenInventory(component);
    }

    private SaveObject? HealthComponent() =>
        Pawn is null ? null : _editor.Resolve(Pawn.Properties.Get<ObjectValue>("mHealthComponent")?.Value);

    private ActorObject RequirePawn() => Pawn ?? throw new InvalidOperationException($"{Name} has no character in this save.");

    private static PlayerIdentity? ReadIdentity(byte[] trailing)
    {
        // Format 0xF1: platform byte, int32 size, id bytes. Other formats (e.g. 3) carry no identity.
        if (trailing.Length < 6 || trailing[0] != IdentityFormat)
            return null;
        int size = BinaryPrimitives.ReadInt32LittleEndian(trailing.AsSpan(2));
        return 6 + size <= trailing.Length ? new PlayerIdentity(trailing[1], trailing.AsSpan(6, size).ToArray()) : null;
    }

    public override string ToString() => Name;
}

public sealed class PlayerService(SaveEditor editor)
{
    public IReadOnlyList<Player> All =>
        editor.OfClass("BP_PlayerState_C").Where(o => o.IsParsed).Select(o => new Player(editor, o)).ToList();
}
