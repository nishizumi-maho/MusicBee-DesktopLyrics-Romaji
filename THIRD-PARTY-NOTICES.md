# Third-party notices

Desktop Lyrics (romaji fork) is licensed under the Apache License 2.0 (see `LICENSE` and `NOTICE`).
The release package also contains the following third-party components, each under its own license.
Their license texts are in the `licenses/` folder of this repository and in `DesktopLyrics/licenses/`
inside the release zip.

## Desktop Lyrics (the original plugin)

- Copyright Charlie Jiang (cqjjjzr). Apache License 2.0.
- Source: <https://github.com/cqjjjzr/MusicBee-DesktopLyrics>
- This repository is a modified version of it; see `NOTICE` and `CHANGELOG.md`.

## RomajiCore (`RomajiCore.dll`, source in `external/RomajiCore`)

- Japanese-to-romaji converter. Copyright 2026 nishizumi-maho. MIT License.
- License text: `licenses/RomajiCore-LICENSE.txt`

## NMeCab (`LibNMeCab.dll`)

- Japanese morphological analyzer for .NET, a port of MeCab.
- Copyright Tsuyoshi Komuta. MeCab: copyright Taku Kudo and Nippon Telegraph and Telephone Corporation.
- License: GPL-2.0-or-later **or** LGPL-2.1-or-later. This plugin uses it under the **LGPL-2.1**
  option: the library ships unmodified as a separate DLL (NuGet package `LibNMeCab` 0.10.2), and you
  may replace it with another compatible build.
- Source: <https://github.com/komutan/NMeCab>
- License texts: `licenses/NMeCab-COPYING.txt`, `licenses/NMeCab-LGPL-2.1.txt`

## IPADIC dictionary (`RomajiLyrics/IpaDic/*`)

- Binary IPADIC dictionary as packaged by NMeCab (NuGet package `LibNMeCab.IpaDicBin` 0.10.0).
- Copyright 2000-2003 Nara Institute of Science and Technology (NAIST). Many entries originate from
  ICOT Free Software.
- License: `licenses/IPADIC-COPYING.txt` (also shipped as `RomajiLyrics/IpaDic/COPYING`).

## Newtonsoft.Json (`Newtonsoft.Json.dll`)

- Copyright James Newton-King. MIT License. <https://www.newtonsoft.com/json>
- License text: `licenses/Newtonsoft.Json-LICENSE.md`

## Cyotek ColorPicker (`Cyotek.Windows.Forms.ColorPicker.dll`)

- Copyright Cyotek Ltd. MIT License. <https://github.com/cyotek/Cyotek.Windows.Forms.ColorPicker>
- License text: `licenses/Cyotek.Windows.Forms.ColorPicker-LICENSE.txt`

## MusicBee plugin API (`src/DesktopLyrics/MusicBeeInterface.cs`)

- The MusicBee plugin interface definition published by Steven Mayall for plugin authors.
- MusicBee: <https://getmusicbee.com>
