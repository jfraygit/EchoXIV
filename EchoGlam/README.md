# EchoGlam

A Dalamud plugin for building glamours in game, sharing them, and wearing anybody else's.

Dress your character from a searchable wardrobe holding every item in the game, dye every slot, edit
the full appearance, and save the result as a named outfit. Then publish the look to a community
catalogue that can be browsed, filtered, voted on and favourited without leaving the game, and put
somebody else's glamour on with one click and a revert that is always one click away.

**No other plugins are required.** EchoGlam does not depend on Glamourer or Penumbra, though it can
read and write Glamourer's design strings so that looks move between the two.

## Installation

Add `https://echoxiv.com/pluginmaster.json` to Dalamud's custom plugin repositories.

## What It Does

- A wardrobe covering every equippable item, filtered by job, by what the character owns, and by how
  hard a piece is to get.
- Dyes on both channels, grouped by colour family, with the last choice carried onto the next piece.
- A full appearance editor driven by the game's own creation data, so a race is only ever offered
  the faces, hairstyles and features it actually has.
- Named outfits, including the appearance if wanted, worn from one list.
- Automatic outfits, which dress the character on entering a map, a particular duty, any duty, or
  combat, and undress again when none of those hold.
- An animation played as an outfit goes on, chosen from any emote or action in the game.
- A gallery of published glamours with screenshots, tags, votes and favourites, and a one-click
  try-on that reverts.
- Friends, who see each other's glamours in the world as they change, swap animation included.
- Import and export of Glamourer designs through the clipboard.

## Where the Data Lives

Outfits, rules and settings are files on the machine, under Dalamud's own configuration directory.

**Publishing is the only thing that sends a glamour anywhere**, and withdrawing one deletes it on the
relay. Nothing about a character is uploaded until somebody presses publish.

Glamour sharing between friends is off until it is switched on. When it is on, the look currently
worn is held in the relay's memory for a few minutes at a time and never written to its disk, so
there is no history of what anybody wore. Friendships are bonds between installations rather than
between characters, and characters are matched through a hashed index, so the relay is never told the
name of somebody's alt.

The list of friends whose glamours are hidden is local and is never sent.

The server side is a separate, private repository. It is not needed to build or run the plugin.

## Building

Requires the .NET 10 SDK and a local Dalamud install. The project references
`%AppData%\XIVLauncher\addon\Hooks\dev\`; override with a `DalamudLibPath` property if yours is
elsewhere.

```
dotnet build -c Release
```

`EchoGlam.Shared` holds the types the plugin and the relay both agree on. It is here because the
plugin does not compile without it.

A Debug build talks to a development relay and a Release build talks to the live one. That is decided
at compile time rather than by a setting, so a shipped build has no code path that reaches the
development instance.

## Source

This repository is the published source for the shipped plugin, kept in step with each release.

## Licence

Copyright (C) 2026 jfraygit.

Licensed under the **GNU Affero General Public License, version 3 or later**. See [LICENSE](LICENSE).

EchoGlam is a Dalamud plugin and links against Dalamud, which is itself AGPL-3.0. It comes with no
warranty, to the extent the licence permits.

Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Not Affiliated with Square Enix

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. EchoGlam is an unofficial, fan-made tool with no
affiliation with or endorsement by Square Enix.
