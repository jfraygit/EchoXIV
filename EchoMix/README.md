# EchoMix

A Dalamud plugin that turns a Final Fantasy XIV character into a live DJ booth: two decks, a
crossfader, EQ and filters, a playlist library, and a broadcast that other players can tune into
from inside the game or from a plain web browser.

## Installation

Add `https://echoxiv.com/pluginmaster.json` to Dalamud's custom plugin repositories.

Open it with `/echomix`, or `/el`/`/emix` to jump straight to the Listener or DJ Deck view.

## What It Does

- **Mixes two decks** with independent gain, EQ, filters and a crossfader, plus a tempo-sync engine
  that time-stretches a track to match the other deck's BPM without changing its pitch.
- **Broadcasts live** to other players in the same game session over a relay, with proximity-based
  audio (only players physically nearby hear the show) as an option alongside open broadcasting.
- **Generates a public web link** for a live show - a branded page that autoplays in any browser
  with its own visualizer, and a raw stream endpoint a non-browser client (another plugin, a
  recording tool) can tap into directly.
- **Plays a curated playlist library** organized into folders, with a searchable queue per deck,
  drag-to-reorder, and sound pads for one-shot effects.
- **Broadcasts Spotify or an external line-in device** instead of the deck library, for a DJ who
  mixes in dedicated software and just wants EchoMix to carry the result.
- **Publishes an optional DJ profile** to a public directory - genre, avatar, socials, a follow
  button, and a page other players can find independent of catching a show live.
- **Takes song requests from listeners**, with per-listener rate limits and a queue the DJ reviews
  before anything loads onto a deck.

## Two Processes, On Purpose

A DJ deck needs real-time audio mixing, WASAPI device access, and an Opus encoder feeding a
network socket at a steady cadence - none of which a Dalamud plugin can do safely inside the
game's own process without risking the game's stability. So the actual mixing engine is
`EchoMix.AudioHost`, a small standalone executable the plugin launches and talks to over a local
named pipe. It ships as `AudioHost/EchoMix.AudioHost.exe` inside the plugin's own release zip -
if that looks unusual for something distributed as a "Dalamud plugin," this is why, and its full
source is right here alongside the plugin's, gated to the same house style and licensed the same
way.

`EchoMix.Shared` is the wire contract both sides agree on - the local pipe protocol between the
plugin and its AudioHost, and the network protocol AudioHost speaks to the relay. It is a project
of its own because the plugin does not compile without it, and because a second, independent copy
of it also lives in the relay's own repository (see below) - the two are kept byte-identical by
hand, not by shared history.

## The Relay

Broadcasting and listening both go through a relay that fans encoded audio out from one host to
every listener, without decoding or re-encoding it along the way. The relay's source is
[jfraygit/EchoMix-Relay](https://github.com/jfraygit/EchoMix-Relay), a separate private
repository - **it is not needed to build or run the plugin**, only to run the server side
yourself. `EchoMix.Shared`'s copy in this repository is what the plugin actually compiles against.

Nothing about listening to a show, joining a room, or running a DJ deck locally needs a network
call beyond that relay connection. A DJ profile lookup, a bug report, and a public show listing
all go through the same relay rather than a separate service.

## Building

Requires the .NET 10 SDK and a local Dalamud install. The projects reference
`%AppData%\XIVLauncher\addon\Hooks\dev\`; override with a `DalamudLibPath` property if yours is
elsewhere.

```
dotnet build -c Release
```

`EchoMix.AudioHost` targets Windows specifically (WASAPI, System Media Transport Controls) and
has no Dalamud reference at all - it can be built and run standalone, including its own
`--listen-test`/`--spotify-test`/`--bpm-test` diagnostic modes documented at the top of its
`Program.cs`.

A Debug build talks to a development relay and a Release build talks to the live one. That is
decided at compile time rather than by a setting, so a shipped build has no code path that reaches
the development instance.

## Source

This repository is the published source for the shipped plugin, kept in step with each release.

## License

Copyright (C) 2026 jfraygit.

Licensed under the **GNU Affero General Public License, version 3 or later**. See [LICENSE](LICENSE).

EchoMix is a Dalamud plugin and links against Dalamud, which is itself AGPL-3.0. It comes with no
warranty, to the extent the license permits.

Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Not Affiliated with Square Enix

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. EchoMix is an unofficial, fan-made tool with no
affiliation with or endorsement by Square Enix.
