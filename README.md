# Satisfactory Save Editor

A desktop save editor for [Satisfactory](https://www.satisfactorygame.com/) 1.1+, written in C# with
[Avalonia](https://avaloniaui.net/). It reads and writes `.sav` files losslessly: a save that is opened and
saved without edits comes out byte-for-byte identical.

> This is a fan-made tool. It is not affiliated with or endorsed by Coffee Stain Studios.
> Always keep a copy of your saves. The editor makes a backup before every save, but a backup you made yourself is better.

## Features

- **Players**: health, position, inventory and equipment slots, with an item picker and stack limits.
- **Map**: every player, container, vehicle, machine, conveyor belt, pipe, power line, foundation, wall,
  pillar and beam drawn to scale. Click anything to inspect or edit it, and Shift+drag to select an area.
- **Containers**: storage, vehicles, machines, crates and drop pods share one inventory editor. Search
  finds containers by the items inside.
- **Dimensional Depot**: edit the network storage (add, change or remove item amounts).
- **Progression**: unlock milestones, MAM research, alternate recipes and AWESOME Shop items, and change
  inventory and hand slot counts.
- **AWESOME Sink**: points per track (standard and DNA) and coupons waiting to be printed.
- **Delete and move**: delete buildings, belts, vehicles and crates. The preview lists everything
  affected: components, attached power lines, conveyor chains and unlinked references. Box-selected
  foundations and walls can also be moved.
- **Explorer**: a generic tree of every object and property in the save, with validated editing, for
  anything the dedicated tabs don't cover.

**Safety:**
- **Change summary:** before saving, you see a readable list of everything you changed.
- **Validation:** the edited save is loaded back and checked before anything is written. A save that
  fails the check is refused and your file stays untouched.
- **Backup:** a timestamped `.bak` is written next to the original before it is overwritten.

## Requirements

- **Windows 10/11**: the app is built with cross-platform Avalonia, but it is only tested on Windows.
- **[.NET 10 SDK](https://dotnet.microsoft.com/download)**.
- **Satisfactory installed**: item, recipe and schematic names come from the game's own
  `CommunityResources/Docs/*.json`, so they always match your game version. The editor finds Steam and
  Epic installs automatically. Otherwise use **File › Locate game data…**. Without game data the editor
  still works, but it shows class names and can't unlock schematics.

## Build and run

```powershell
git clone <this repository>
cd satisfactory-save-editor
dotnet run --project src/SatisfactorySaveEditor.App
```

Open a save with **File › Open…**. Saves are usually in
`%LOCALAPPDATA%\FactoryGame\Saved\SaveGames\<account id>\`.

Tips:
- Use **File › Save As…** with a new name while experimenting, then load that save in the game.
- On the map, untick layers to make selection easier. For example, hide buildings to box-select only foundations.

## Supported saves

- **Version:** saves from **Satisfactory 1.1 and later**: save header version 14, save version 60 (Unreal Engine 5.6).
- **Older objects:** objects that older versions carried forward in a save (legacy property tags) are
  read and written too.
- **Older saves:** other versions are refused rather than risk corrupting them.

## What is verified in game

These edits have been loaded in the game and confirmed to work:
- inventory and container edits;
- schematic unlocks (milestones, MAM, AWESOME Shop) including slot upgrades;
- AWESOME Sink coupons;
- Dimensional Depot contents;
- deleting buildings, belts (their conveyor chain is rebuilt by the game) and foundations;
- moving foundations.

Unit tests prove that edits round-trip and validate. The game's acceptance has only been checked for the
list above.

## Known limitations

- **Unlocks the save can't record:** some schematic unlocks can't be applied, such as items given on
  purchase, scanner entries, Dimensional Depot upgrades and production boost. The Progression tab reports them as "not applied".
- **Moving buildings:** only foundations, walls, ramps, walkways, pillars and beams can be moved.
  Machines stay in place, because their belts and wires would be left behind.
- **Map-placed objects:** objects that are part of the map itself (crash sites, for example) can't be
  deleted. The game would recreate them.
- **Approximate map sizes:** structure footprints are derived from class names, so unusual pieces are drawn as 4 m squares.
- **No icons:** the map has no terrain image, and items have no icons.

## How it works

The save format is documented on the [Satisfactory wiki](https://satisfactory.wiki.gg/wiki/Save_files)
up to save version 46. Version 60 differs in many places, and those parts were worked out from real saves:
- the per-level versioning blocks;
- the new UE 5.4+ property tags;
- conveyor chain, power line and lightweight buildable data.

Every value is parsed with a self-check. A property or object is only kept as typed data if it
re-serializes to exactly the original bytes. Anything else is kept as raw bytes, so an unknown detail can
never damage a save.

```
src/
  SatisfactorySaveEditor.Core     Binary format: header, compression, levels, objects, properties, trailing data
  SatisfactorySaveEditor.Domain   Game concepts: players, inventories, progression, map, deletion, sink, depot
  SatisfactorySaveEditor.App      Avalonia desktop app
  SatisfactorySaveEditor.Cli      Developer tool: info, stats, dump, round-trip checks
tests/
  SatisfactorySaveEditor.Core.Tests
  SatisfactorySaveEditor.Domain.Tests
assets/                           Sample saves used by the tests (personal identifiers removed)
```

## Tests

```powershell
dotnet test
```

The tests run against the sample saves in `assets/`. They cover lossless round-trips, full parsing,
validation and each editing feature (save, reload, compare). The Domain tests use a small handwritten
game-data fixture, so they do not need the game installed.

## Developer CLI

```powershell
dotnet run --project src/SatisfactorySaveEditor.Cli -- info  path/to/save.sav            # summary
dotnet run --project src/SatisfactorySaveEditor.Cli -- roundtrip-check path/to/save.sav  # lossless check
dotnet run --project src/SatisfactorySaveEditor.Cli -- stats path/to/save.sav            # parse coverage
dotnet run --project src/SatisfactorySaveEditor.Cli -- dump  path/to/save.sav Char_Player  # inspect objects
```

If a save fails `roundtrip-check` or `stats` reports unparsed data, please open an issue. Remove personal
data from the save before sharing it.
