# EchoNav

A Dalamud plugin for **Occult Crescent: North Horn**. It lists the Critical Encounters and FATEs
running right now and walks you to any of them with one click, choosing for itself between walking,
an aetheryte hop and Occult Return.

It also tracks the pot cycle, keeps knowledge-crystal buffs topped up, steps around monsters on the
way, and picks up coffers it passes.

**It does nothing outside North Horn.** No table reads, no collision parsing, no steering. One list
in `Nav/SupportedZones.cs` gates the entire plugin.

## Installation

Add `https://echoxiv.com/pluginmaster.json` to Dalamud's custom plugin repositories.

## How It Gets You There

It builds its own navigation mesh rather than depending on another plugin being installed, which was
a hard requirement from the start. Collision geometry is extracted from the game's own files through
Lumina and turned into a mesh with Recast; routes are queried with Detour. North Horn ships prebuilt,
because building one takes the better part of a minute and several gigabytes of working set - not
something to do on the frame a player zones in.

Movement is done by hooking the game's own "read movement input" function and writing a
camera-relative direction into it, rather than by synthesising key presses. The player's keybinds are
irrelevant, the window does not need focus, and the direction is continuous rather than quantised to
the eight directions WASD can express. Touching the controls at any point stops the run outright.

## Building

Requires the .NET 10 SDK and a local Dalamud install (the project references
`%AppData%\XIVLauncher\addon\Hooks\dev\`; override with a `DalamudLibPath` property if yours is
elsewhere).

```
dotnet build -c Release
```

## Source

This repository is the published source for the shipped plugin, kept in step with each release.

## Licence

Copyright (C) 2026 jfraygit.

Licensed under the **GNU Affero General Public License, version 3 or later**. See [LICENSE](LICENSE).

EchoNav is a Dalamud plugin and links against Dalamud, which is itself AGPL-3.0. It comes with no
warranty, to the extent the licence permits.

Third-party notices, including the MIT-licensed work this plugin adapts, are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Not Affiliated with Square Enix

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. EchoNav is an unofficial, fan-made tool with no
affiliation with or endorsement by Square Enix.
