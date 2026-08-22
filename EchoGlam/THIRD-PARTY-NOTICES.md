# Third-Party Notices

EchoGlam uses the following third-party work. This file is the notice those licences require, and it
is kept here rather than in source comments so that it survives into every published copy.

---

## System.Drawing.Common - MIT

[dotnet/winforms](https://github.com/dotnet/winforms), used as the `System.Drawing.Common` NuGet
package and **redistributed inside the plugin** along with the runtime assemblies it pulls in
(`System.Private.Windows.Core`, `System.Private.Windows.GdiPlus` and `Microsoft.Win32.SystemEvents`).

It crops, scales and encodes the screenshots attached to a published glamour, so the player chooses
their own framing and the upload is measured in tens of kilobytes rather than megabytes. The package
is used unmodified.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

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

## Referenced but Not Redistributed

These are compiled against and loaded from the user's existing Dalamud installation. EchoGlam ships
no copy of any of them (`<Private>false</Private>` in the project file), so no notice is required,
but they are listed because the plugin does not run without them.

| | |
|---|---|
| [Dalamud](https://github.com/goatcorp/Dalamud) | AGPL-3.0 - the reason this project is AGPL-3.0 as well |
| Dalamud.Bindings.ImGui | part of Dalamud - the immediate-mode interface every window is drawn with |
| [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) | MIT - the client structures the equipment and appearance code write through |
| InteropGenerator.Runtime | MIT - types FFXIVClientStructs' generated members expose |
| [Lumina](https://github.com/NotAdam/Lumina) and Lumina.Excel | MIT - items, dyes, jobs, maps, icons and the character creation data are read from the game's own sheets |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT |

---

## Glamourer Design Strings

EchoGlam reads and writes the share strings produced by
[Glamourer](https://github.com/Ottermandias/Glamourer)'s clipboard button. **No Glamourer code is
used, referenced or redistributed**, and the plugin does not require Glamourer to be installed. The
format was determined by decoding a real string, and the reader is EchoGlam's own.

---

## Game Assets

Item icons, dye swatches and the character creation artwork are the game's own art. Nothing is
bundled: every icon is rendered out of the player's own installation through Lumina. Screenshots
attached to a published glamour are taken by the player on their own machine, of their own character.
No asset is copied, redistributed or hosted, and there is nothing here that a player does not already
own.

---

## No Adapted Code

No signature pattern, algorithm or snippet in this plugin is adapted from another project. Where a
technique was learned from a sibling plugin in this suite, that plugin has the same author and the
same licence.

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. EchoGlam is an unofficial, fan-made tool and is not
affiliated with or endorsed by Square Enix.
