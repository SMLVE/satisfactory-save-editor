# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/), and versions follow
[Semantic Versioning](https://semver.org/). While the version is 0.x, features and behaviour may still change.

## [Unreleased]

### Fixed
- Opening a damaged save, or choosing a file that isn't the game's Docs JSON, shows an error instead of
  closing the app.
- An unlock that can't be fully applied (for example, a player without an inventory) no longer leaves a
  partial unlock in the save that the change summary doesn't list.

## [0.1.0] - 2026-10-18

First public release.

### Added

**Save format and safety**
- Lossless reading and writing of Satisfactory 1.1+ saves (header version 14, save version 60). An unedited
  save round-trips byte for byte.
- Self-checking parser: values are kept as typed data only when they re-serialize exactly, otherwise as raw
  bytes.
- Decoding of class-specific data:
  - power lines;
  - foundations, walls, ramps, walkways, pillars and beams (lightweight buildables);
  - conveyor chains and belt contents.
- Pre-save validation: the edited save is loaded back and checked before anything is written.
- Change summary before saving.
- Timestamped backup of the previous file on every save.

**Editing**
- **Players:** health, position, inventory and equipment slots, with an item picker and stack limits.
- **Map:**
  - every object to scale, with layers;
  - belts and pipes drawn along their splines;
  - power lines, foundations, pillars and beams;
  - click to inspect, Shift+drag to select an area.
- **Containers:** storage, vehicles, machines, crates and drop pods in one inventory editor, with search by contents.
- **Dimensional Depot:** network storage editor.
- **Progression:**
  - unlock milestones, MAM research, alternate recipes and AWESOME Shop items;
  - inventory and hand slot counts.
- **AWESOME Sink:** points per track and coupons to print.
- **Delete:**
  - buildings, belts, vehicles and crates;
  - a preview of components, attached power lines, conveyor chains and unlinked references.
- **Move:** move selected foundations, walls and other structures.
- **Explorer:** generic tree of every object and property, with validated editing.

**Tooling**
- Game data (item, recipe and schematic names) read from the installed game's Docs JSON.
- Developer CLI: `info`, `stats`, `dump`, `roundtrip-check` and more.
- Test suite covering round-trips, parsing, validation and every editing feature on three sample saves.

[Unreleased]: https://github.com/SMLVE/satisfactory-save-editor/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/SMLVE/satisfactory-save-editor/releases/tag/v0.1.0
