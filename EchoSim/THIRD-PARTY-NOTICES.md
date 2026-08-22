# Third-Party Notices

This file is kept here rather than in source comments so that it survives into every published copy.

## Nothing Is Redistributed

**EchoSim ships no third-party code.** The four project files in this repository contain no
`PackageReference` at all, so the package that installs into Dalamud carries EchoSim's own assemblies
and nothing else.

That is why there is no licence text reproduced below. A notice obligation follows what a project
redistributes, and this one redistributes nothing.

---

## Referenced but Not Redistributed

These are compiled against and loaded from the user's existing Dalamud installation
(`<Private>false</Private>` in the project file). No copy of any of them is shipped, so no notice is
required, but they are listed because the plugin does not run without them.

| | |
|---|---|
| [Dalamud](https://github.com/goatcorp/Dalamud) | AGPL-3.0 - the reason this project is AGPL-3.0 as well |
| Dalamud.Bindings.ImGui | part of Dalamud - the immediate-mode interface every window is drawn with |
| [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) | MIT - the client structures the equipped gear, melds and combat state are read through |
| [Lumina](https://github.com/NotAdam/Lumina) and Lumina.Excel | MIT - actions, items, materia, foods, jobs and duties are read from the game's own sheets |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT |

---

## Game Data

Every potency, recast, action id, item stat and duty id in this repository is read out of the
player's own game installation through Lumina, or was dumped from it and transcribed with the dump as
the record. None of it is copied from a third-party database, and no sheet, icon or asset is bundled.

There is nothing here that a player does not already own.

---

## FFLogs

Log analysis and leaderboard corroboration read public reports from the
[FFLogs](https://www.fflogs.com) v2 API. No FFLogs code is used or redistributed, and the plugin
holds no API credentials: report queries are made by the server side, which is a separate private
repository, using its own registered client.

Only reports a user asks about, or that correspond to a score somebody submitted, are ever read, and
only reports their owner has already made public are readable at all.

---

## Guides Consulted

Rotation priorities and opener orderings were checked against
[The Balance](https://www.thebalanceffxiv.com) and [Icy Veins](https://www.icy-veins.com), and a
handful of comments quote those guides directly with attribution where the wording is the point.

**No code was taken from either.** They are written for people, not for machines. Where the two
disagree, the disagreement is settled against a real log rather than by preferring one source, and
the comment at that decision says which log and why.

---

## No Adapted Code

No signature pattern, algorithm or snippet in this plugin is adapted from another project. Where a
technique was learned from a sibling plugin in this suite, that plugin has the same author and the
same licence.

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. EchoSim is an unofficial, fan-made tool and is not
affiliated with or endorsed by Square Enix.
