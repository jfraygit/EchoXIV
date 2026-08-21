# Third-Party Notices

EchoNav uses the following third-party work. This file is the notice those licences require, and it
is kept here rather than in source comments so that it survives into every published copy.

---

## FFXIV-RaidsRewritten - MIT

Three signature byte patterns and the shape of the movement-input hook in
`EchoNav.Plugin/Nav/MovementOverride.cs` are adapted from
[Ricimon/FFXIV-RaidsRewritten](https://github.com/Ricimon/FFXIV-RaidsRewritten)
(`DalamudPlugin/RaidsRewritten/Interop/PlayerMovementOverride.cs`), which credits
[awgil/ffxiv_navmesh](https://github.com/awgil/ffxiv_navmesh) and
[Caraxi/SimpleTweaksPlugin](https://github.com/Caraxi/SimpleTweaksPlugin) in turn.

```
MIT License

Copyright (c) 2025 Ricimon

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

---

## DotRecast - zlib

[ikpil/DotRecast](https://github.com/ikpil/DotRecast), a C# port of Recast Navigation, used as the
`DotRecast.Recast` and `DotRecast.Detour` NuGet packages and redistributed inside the plugin. EchoNav
builds its navigation mesh with Recast and queries it with Detour. The packages are used unmodified.

```
Copyright (c) 2023-2025 Choi Ikpil (ikpil@naver.com)

This software is provided 'as-is', without any express or implied warranty. In no event will the
authors be held liable for any damages arising from the use of this software.

Permission is granted to anyone to use this software for any purpose, including commercial
applications, and to alter it and redistribute it freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not claim that you wrote the
   original software. If you use this software in a product, an acknowledgment in the product
   documentation would be appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be misrepresented as being
   the original software.
3. This notice may not be removed or altered from any source distribution.
```

---

## Referenced but Not Redistributed

These are compiled against and loaded from the user's existing Dalamud installation. EchoNav ships no
copy of any of them (`<Private>false</Private>` in the project file), so no notice is required, but
they are listed because the plugin does not run without them.

| | |
|---|---|
| [Dalamud](https://github.com/goatcorp/Dalamud) | AGPL-3.0 - the reason this project is AGPL-3.0 as well |
| [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) | MIT |
| [Lumina](https://github.com/NotAdam/Lumina) and Lumina.Excel | MIT - also does all of the `.pcb` collision-file parsing |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT |

---

## Not Used

[awgil/ffxiv_navmesh](https://github.com/awgil/ffxiv_navmesh) (vnavmesh) and Lifestream are both
widely used for this kind of work and neither ships a licence file, so no code from either was read
or copied. EchoNav builds its own navigation mesh instead, which is why it has no dependency on
another plugin being installed. The collision-file format is read through Lumina's public
`PcbResourceFile` rather than through anyone's reverse-engineered parser.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. EchoNav is an unofficial, fan-made tool and is not
affiliated with or endorsed by Square Enix.
