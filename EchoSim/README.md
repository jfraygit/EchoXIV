# EchoSim

A Dalamud plugin that simulates a full-length fight for your job, then shows you exactly where the
damage came from and how your real kills measure up against it.

EchoSim models twenty-one jobs at the level of individual button presses: every action's potency and
recast, the global cooldown your gear actually gives you, animation locks, resource gauges, combo
state, damage-over-time ticks and the raid buffs a normal party brings. It runs that model for a full
fight and reports the DPS, a breakdown by action, and the timeline that produced it.

## Installation

Add `https://echoxiv.com/pluginmaster.json` to Dalamud's custom plugin repositories.

Open it with `/echosim` or `/es`.

## What It Does

- **Simulates your job** at your own equipped gear, or at a best-in-slot preset, for any fight
  length.
- **Compares gear, melds and consumables** by rerunning the simulation with one thing changed, so a
  suggestion is a measured difference rather than a stat weight.
- **Solves melds** for a set of gear, against the simulation rather than against a formula.
- **Checks openers and burst windows** against the modelled rotation and names what is out of place.
- **Analyses an FFLogs report** cast by cast against the simulated ceiling, so a parse is turned into
  a list of specific things that happened rather than a percentile.
- **Scores execution** from 0 to 100: what fraction of your job's own ceiling your button-pressing
  achieved, at your gear. This is not an FFLogs percentile and does not pretend to be one.
- **Leaderboards** for supported encounters, per job, where a kill can be submitted and ranked.

## The Ceiling Is a Model, Not a Promise

The simulated number is what perfect play at that gear would produce under this model's assumptions:
a stationary target, no deaths, and a fight that never makes you stop pressing buttons. Real fights
have movement, mechanics and downtime, so a real kill scoring below the ceiling is the normal case
rather than a mistake.

Where the model is known to be incomplete, the plugin says so in place rather than quietly rounding
the difference away. Healers in particular cannot reach 100, because the model does not price the
healing they are actually there to do.

Potencies, recasts and action ids are read out of the game's own data rather than transcribed by
hand. Rotation decisions are checked against real logs at the same fight length before they are
accepted.

## Leaderboards and What Gets Sent

Nothing leaves the machine unless you submit a score. Simulation, gear comparison and meld solving
are entirely local and need no network at all.

Submitting a score sends the kill: your character name and world, the job, the encounter, the score
and the cast sequence behind it. A submission is then corroborated against the public FFLogs record
of that kill before it is ranked, and a score that cannot be corroborated is shown to you and to
nobody else.

A profile exists only for characters that have submitted, and shows nothing about anyone who has not.

Bug reports are sent only when you type one and press send.

The server side is a separate, private repository. It is not needed to build or run the plugin.

## Building

Requires the .NET 10 SDK and a local Dalamud install. The project references
`%AppData%\XIVLauncher\addon\Hooks\dev\`; override with a `DalamudLibPath` property if yours is
elsewhere.

```
dotnet build -c Release
```

`EchoSim.Shared` holds the types the plugin and the relay both agree on. It is here because the
plugin does not compile without it.

The simulation engine under `EchoSim.Plugin/Sim` has no Dalamud references at all, so it can be read,
compiled and reasoned about without the game in the way.

A Debug build talks to a development relay and a Release build talks to the live one. That is decided
at compile time rather than by a setting, so a shipped build has no code path that reaches the
development instance.

## Source

This repository is the published source for the shipped plugin, kept in step with each release.

The test harness that validates the engine against real parses is not part of it. That is a separate
program, is not shipped, and is not needed to build or audit anything here.

## Licence

Copyright (C) 2026 jfraygit.

Licensed under the **GNU Affero General Public License, version 3 or later**. See [LICENSE](LICENSE).

EchoSim is a Dalamud plugin and links against Dalamud, which is itself AGPL-3.0. It comes with no
warranty, to the extent the licence permits.

Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Not Affiliated with Square Enix

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. EchoSim is an unofficial, fan-made tool with no
affiliation with or endorsement by Square Enix.
