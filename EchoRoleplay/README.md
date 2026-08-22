# EchoRoleplay

A Dalamud plugin that gives your character a roleplay profile and lets you read everybody else's.

Write a full sheet - name, appearance, personality, backstory, rumours and hooks - and set statuses
that hang under your character for others to read at a glance. Rest the pointer on anybody running
the plugin to read who they are, right-click for the whole sheet, and keep private notes on everyone
you have met.

**There is no search and no directory, deliberately.** The only person you can look up is the one
standing in front of you. That is the whole shape of the plugin rather than a feature that has not
been built yet.

## Installation

Add `https://echoxiv.com/pluginmaster.json` to Dalamud's custom plugin repositories.

## What It Does

- A profile with about thirty fields, all optional, plus your own custom ones for anything the form
  does not cover.
- A portrait you crop and zoom in game, and a colour your name is read in.
- Several profiles per character, with one worn at a time.
- Statuses in your own words, with an icon from the game's own art and a timer that clears them.
- Roleplay names shown over characters instead of their own, on your screen only.
- A theme song chosen from the game's own music, played when somebody opens your profile.
- Contacts, remembering everyone you have read, with private notes that never leave the machine.
- Friends, favourites and a block list. Blocking works both ways: somebody you block stops seeing
  you as well.
- Lodestone verification, so nobody else can claim your character.

## Where the Data Lives

Profiles are files on your own machine. Publishing one sends it to a relay so that other players can
read it, and withdrawing it deletes it there.

**Contacts and private notes never leave the machine.** They are a record of who you have looked up,
which is nobody else's business, and no type in `EchoRoleplay.Shared` describes them - so whether
something can be sent is answerable by checking which project a type lives in.

The relay is told as little as it can be. Nearby characters are matched locally against a hashed
index, so it never learns who is standing near whom, and profile cards are fetched in batches
covering everyone visible rather than one request per person.

The server side is a separate, private repository. It is not needed to build or run the plugin.

## Building

Requires the .NET 10 SDK and a local Dalamud install. The project references
`%AppData%\XIVLauncher\addon\Hooks\dev\`; override with a `DalamudLibPath` property if yours is
elsewhere.

```
dotnet build -c Release
```

A Debug build talks to a development relay and a Release build talks to the live one. That is decided
at compile time rather than by a setting, so a shipped build has no code path that reaches the
development instance.

## Source

This repository is the published source for the shipped plugin, kept in step with each release.

## Licence

Copyright (C) 2026 jfraygit.

Licensed under the **GNU Affero General Public License, version 3 or later**. See [LICENSE](LICENSE).

EchoRoleplay is a Dalamud plugin and links against Dalamud, which is itself AGPL-3.0. It comes with
no warranty, to the extent the licence permits.

Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Not Affiliated with Square Enix

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. EchoRoleplay is an unofficial, fan-made tool with no
affiliation with or endorsement by Square Enix.
