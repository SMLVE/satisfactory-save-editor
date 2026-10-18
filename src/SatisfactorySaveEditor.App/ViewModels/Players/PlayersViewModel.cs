using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.App.ViewModels.Inventory;
using SatisfactorySaveEditor.Domain.Players;

namespace SatisfactorySaveEditor.App.ViewModels.Players;

public sealed partial class PlayersViewModel : ObservableObject
{
    public PlayersViewModel(SaveDocument document, string? selectPlayer = null)
    {
        var items = ItemChoice.From(document.Editor.GameData);
        HasGameData = document.Editor.GameData is not null;
        Players = document.Editor.Players.All.Select(p => new PlayerViewModel(p, document, items)).ToList();
        SelectedPlayer = Players.FirstOrDefault(p => p.Name == selectPlayer)
                         ?? Players.FirstOrDefault(p => p.HasPawn) ?? Players.FirstOrDefault();
    }

    public bool HasGameData { get; }
    public IReadOnlyList<PlayerViewModel> Players { get; }

    [ObservableProperty] private PlayerViewModel? _selectedPlayer;
}

public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly Player _player;
    private readonly SaveDocument _document;

    public PlayerViewModel(Player player, SaveDocument document, IReadOnlyList<ItemChoice> items)
    {
        _player = player;
        _document = document;
        if (player.Pawn is null)
            return;

        Inventory = new InventoryEditorViewModel(
        [
            new InventoryChoice("Inventory", player.Inventory),
            .. player.EquipmentSlots.Select(kv => new InventoryChoice($"{kv.Key} equipment", kv.Value)),
        ], items, change => document.RecordChange($"{Name}: {change}"));
        _healthText = Format(player.Health);
        var position = player.Position;
        (_positionX, _positionY, _positionZ) = (Format(position.X), Format(position.Y), Format(position.Z));
    }

    public string Name => _player.Name;
    public string Detail => _player.Pawn is null ? "No character in this save" : $"Health {Format(_player.Health)}";
    public bool HasPawn => _player.Pawn is not null;
    public InventoryEditorViewModel? Inventory { get; }

    [ObservableProperty] private string _healthText = "";
    [ObservableProperty] private string _positionX = "";
    [ObservableProperty] private string _positionY = "";
    [ObservableProperty] private string _positionZ = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    [RelayCommand]
    private void ApplyHealth() => Try(() =>
    {
        string before = Format(_player.Health);
        _player.Health = ParseFloat(HealthText, "Health");
        HealthText = Format(_player.Health);
        OnPropertyChanged(nameof(Detail));
        return ($"Health {before} → {HealthText}.", HealthText != before);
    });

    [RelayCommand]
    private void ApplyPosition() => Try(() =>
    {
        var before = _player.Position;
        _player.Position = new Vector3(ParseFloat(PositionX, "X"), ParseFloat(PositionY, "Y"), ParseFloat(PositionZ, "Z"));
        return ($"Position {Format(before)} → {Format(_player.Position)}.", _player.Position != before);
    });

    /// <summary>Runs an edit, shows its message and records it in the change log when something changed.</summary>
    private void Try(Func<(string Message, bool Changed)> edit)
    {
        try
        {
            var (message, changed) = edit();
            Message = message;
            MessageIsError = false;
            if (changed)
                _document.RecordChange($"{Name}: {message}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            Message = ex is ArgumentOutOfRangeException range ? range.Message.Split(" (Parameter")[0] : ex.Message;
            MessageIsError = true;
        }
    }

    private static float ParseFloat(string text, string field) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? value
            : throw new FormatException($"{field} must be a number.");

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Format(Vector3 v) => $"({Format(v.X)}, {Format(v.Y)}, {Format(v.Z)})";
}
