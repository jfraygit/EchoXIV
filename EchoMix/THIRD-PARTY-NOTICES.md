# Third-Party Notices

EchoMix redistributes the packages below. This file is the notice those licenses require, and it
is kept here rather than in source comments so that it survives into every published copy.

The obligation follows what is redistributed, not what is referenced: a package with
`<Private>false</Private>` in the project file is loaded from the player's existing Dalamud
installation and ships no copy of its own, while a plain `PackageReference` is compiled into
`EchoMix.Plugin` or `EchoMix.AudioHost` and ships inside the release zip. The distinction is made
per package below.

---

## SoundTouch.Net 2.3.2 - LGPL-2.1-or-later

[owoudenberg/soundtouch.net](https://github.com/owoudenberg/soundtouch.net), copyright (c) Olaf
Woudenberg 2011-2019, **redistributed inside the release as its own separate file,
`SoundTouch.Net.dll`, next to `EchoMix.AudioHost.exe`**. It provides the time-stretch/pitch-shift
engine behind the deck tempo-sync feature (see `TimeStretchSampleProvider`, `DeckEngine`,
`BpmAnalyzer`). Used unmodified.

This is the only copyleft dependency anywhere in this project, and it is worth being explicit
about how its two obligations are met rather than leaving either to be inferred.

**Compatibility.** The rest of this project ships under AGPL-3.0. LGPL-2.1 alone does not combine
cleanly with that, but SoundTouch.Net is licensed **LGPL-2.1-or-later**, and the "or later" clause
is what closes the gap: LGPL-2.1-or-later permits taking the library under LGPL-3.0 instead, and
LGPL-3.0 section 3 explicitly permits conveying a work that combines it with code under GPL-3 (and
by extension AGPL-3, which section 13 of the AGPL treats as GPL-3-compatible for this purpose).
Compatibility is not automatic here; it is a real chain that depends on that one clause, which is
why it is spelled out rather than assumed.

**The relink obligation.** The LGPL is not satisfied by attribution alone - a user has to be able
to replace the library with a modified version and have the result still work. This project meets
that requirement by construction rather than by a separate mechanism: `SoundTouch.Net.dll` is
never merged into `EchoMix.AudioHost.exe` (no ILMerge, no single-file publish that embeds it,
no trimming that could inline it). It sits next to the executable as its own ordinary .NET
assembly, loaded by the normal probing path, so replacing that one file with a differently-built
`SoundTouch.Net.dll` exposing the same public API is sufficient to relink a modified copy - no
recompilation of `EchoMix.AudioHost` itself is required.

```
                  GNU LESSER GENERAL PUBLIC LICENSE
                       Version 2.1, February 1999

 Copyright (C) 1991, 1999 Free Software Foundation, Inc.
 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 Everyone is permitted to copy and distribute verbatim copies
 of this license document, but changing it is not allowed.

[This library is licensed under the GNU Lesser General Public License, version 2.1, as published
by the Free Software Foundation - see https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html for
the full text, reproduced here as required by that license.]

  This library is free software; you can redistribute it and/or modify it under the terms of the
GNU Lesser General Public License as published by the Free Software Foundation; either version
2.1 of the License, or (at your option) any later version.

  This library is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU Lesser General Public License for more details.

  You should have received a copy of the GNU Lesser General Public License along with this
library; if not, write to the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
Boston, MA 02110-1301 USA, or see <https://www.gnu.org/licenses/>.
```

---

## Concentus 2.2.2 - BSD-3-Clause

[lostromb/concentus](https://github.com/lostromb/concentus), **redistributed inside the release**
(`Concentus.dll`, `EchoMix.AudioHost`). A managed port of the Opus reference encoder/decoder, used
for the audio codec on the broadcast wire. Used unmodified.

```
Copyright (c) by various holding parties, including (but not limited to):
Skype Limited, Xiph.Org Foundation, CSIRO, Microsoft Corporation,
Jean-Marc Valin, Gregory Maxwell, Mark Borgerding, Timothy B. Terriberry,
Logan Stromberg. All rights are reserved by their respective holders.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


This repository and its redistributable packages contain independently compiled
versions of the Opus C reference library, which is maintained by Xiph.org and the
Opus open-source contributors. The source code for these libraries is freely available
at https://gitlab.xiph.org/xiph/opus/-/tags/v1.5.2, and all binaries are being
redistributed to you under the same terms of the general Opus license dictated above.
```

---

## NAudio 2.2.1 - MIT

[naudio/NAudio](https://github.com/naudio/NAudio), copyright Mark Heath, **redistributed inside
the release**. The Windows audio engine underneath every deck, effect and capture path in
`EchoMix.AudioHost`. This entry covers the `NAudio` metapackage together with the constituent
assemblies it pulls in from the same repository under the same license - `NAudio.Core`,
`NAudio.Wasapi`, `NAudio.Asio`, `NAudio.Midi`, `NAudio.WinMM` and `NAudio.WinForms` - which are not
separate projects, just the same release split into per-backend assemblies. Used unmodified.

```
Copyright 2020 Mark Heath

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

## NAudio.Vorbis 1.5.0 and NVorbis 0.10.4 - MIT

[naudio/Vorbis](https://github.com/naudio/Vorbis) and [NVorbis/NVorbis](https://github.com/NVorbis/NVorbis),
copyright Andrew Ward, **both redistributed inside the release**. NAudio.Vorbis wraps NVorbis - a
managed Ogg Vorbis decoder - as an `NAudio` sample provider, used for playlist tracks encoded as
Ogg Vorbis. NVorbis ships alongside it as a transitive dependency (`NVorbis.dll` in the release
zip), not something added directly, but it is a distinct package with its own copyright line and
is named here for that reason. Both used unmodified.

```
MIT License

Copyright (c) 2020 Andrew Ward

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Newtonsoft.Json 13.0.3 - MIT

[JamesNK/Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json), copyright (c) 2007 James
Newton-King. `EchoMix.Plugin` loads this from the player's own Dalamud installation and ships no
copy of its own (`<Private>false</Private>`) - required for assembly-identity reasons, since the
plugin shares a process with Dalamud and a second copy of this exact assembly there would be a
real type-identity conflict, not just a version mismatch. `EchoMix.AudioHost` is a separate,
standalone process with no such constraint, but references the same physical file for consistency
and **does redistribute its own copy** (`<Private>true</Private>`, `Newtonsoft.Json.dll` in the
release zip next to `EchoMix.AudioHost.exe`). Used unmodified.

```
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

---

## System.Drawing.Common 10.0.0 - MIT

[dotnet/winforms](https://github.com/dotnet/winforms), copyright (c) .NET Foundation and
Contributors, **redistributed inside the release** (`EchoMix.Plugin`) along with the runtime
assemblies it pulls in: `Microsoft.Win32.SystemEvents` (its own separate package, same license,
copyright Microsoft Corporation), and `System.Private.Windows.Core`/`System.Private.Windows.GdiPlus`
(bundled inside the `System.Drawing.Common` package itself, not separate package IDs). Used to
crop, scale and encode DJ profile/show images before upload, so a player chooses their own framing
and the upload stays small. Used unmodified.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Windows SDK Projection Assemblies - Not a Third-Party Package

`Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` also ship inside `EchoMix.AudioHost`'s
release output, used for the Spotify now-playing reader's System Media Transport Controls access.
Neither is a `PackageReference` this project added - both come from the .NET SDK's own Windows
targeting support, resolved automatically because `EchoMix.AudioHost` targets
`net10.0-windows10.0.19041.0`. They are part of the same MIT-licensed `dotnet/dotnet` platform as
the .NET runtime itself, not a separate third-party choice, and are named here for completeness
rather than given their own notice section.

---

## Referenced but Not Redistributed

These are compiled against and loaded from the player's existing Dalamud installation
(`<Private>false</Private>` in the project file). No copy of any of them is shipped, so no notice
is required, but they are listed because the plugin does not run without them.

| | |
|---|---|
| [Dalamud](https://github.com/goatcorp/Dalamud) | AGPL-3.0 - the reason this project is AGPL-3.0 as well |
| Dalamud.Bindings.ImGui | part of Dalamud - the immediate-mode interface every window is drawn with |
| [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) | MIT |
| [Lumina](https://github.com/NotAdam/Lumina) and Lumina.Excel | MIT |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT - loaded from Dalamud by `EchoMix.Plugin` specifically; see its own entry above for the separate copy `EchoMix.AudioHost` redistributes |

---

## No Adapted Code

No signature pattern, algorithm or snippet in this plugin is adapted from another project. Where a
technique was learned from a sibling plugin in this suite, that plugin has the same author and the
same license.

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. EchoMix is an unofficial, fan-made tool and is not
affiliated with or endorsed by Square Enix.
