# Desktop Lyrics for MusicBee (romaji fork)

A fork of [**Desktop Lyrics**](https://github.com/cqjjjzr/MusicBee-DesktopLyrics) by **Charlie Jiang**
([add-on page](https://www.getmusicbee.com/addons/plugins/171/desktop-lyrics/)). The original shows
synced lyrics floating on your desktop. All credit for it goes to its author. This fork builds on
version 1.10.1 and adds the following:

- **Romaji under Japanese lyrics.** The second line shows the romaji of the current line. The romaji
  engine and its IPAdic dictionary come with the plugin, so there is nothing else to install. Turn it
  on or off from the lyrics right-click menu or the settings.
- **Smooth line transitions.** The old line slides up and out while the new one rises into place.
  The main line and the romaji move together. You can set the duration (200–1500 ms) or turn it off.
- **Adjustable animation frame rate**, from 30 fps up to your monitor's refresh rate (default 60).
- **Lyrics opacity** (10–100 %) for the whole overlay, plus quick presets in the right-click menu.
- **A separate colour for the second line** (romaji or translation).
- **Single box background**: one rounded box behind both lines, using the background opacity setting.
- **Plain, untimed lyrics are shown too**, one line every three seconds. The original only showed
  synced LRC lyrics.
- **Update notifications.** The plugin checks this repository's releases and offers to install new
  versions. See [Updates](#updates).
- **No embedded DLLs.** Dependencies ship as normal files rather than being packed into the plugin.
  The packing used to cause false positives in some antivirus scanners.
- Several bug fixes. See [CHANGELOG.md](CHANGELOG.md).

![Lyrics with romaji in a single box](docs/screenshots/desktop-lyrics-box.png)

Fonts, gradient colours, border, alignment, auto-hide and drag-to-move work as in the original.

## Requirements

- Windows with .NET Framework 4.8
- MusicBee 3.6 or later
- Synced (LRC) lyrics or plain lyrics, from your files or from any lyrics provider plugin

## Installation

1. Download `MusicBee-DesktopLyrics-Romaji-x.y.z.zip` from
   [Releases](https://github.com/nishizumi-maho/MusicBee-DesktopLyrics-Romaji/releases/latest).
2. Close MusicBee.
3. Extract **everything** in the zip into MusicBee's Plugins folder, keeping the folders. Replace
   files if asked.
   - Installer version: `%APPDATA%\MusicBee\Plugins`
   - Portable version: `<MusicBee folder>\Plugins`
4. Start MusicBee. If you used the original Desktop Lyrics before, your settings are kept.

Show or hide the lyrics with **View → Desktop Lyrics**. Right-click the lyrics to open the settings,
switch romaji on or off, change the opacity, or check for updates. The settings are also in
*Edit → Edit Preferences → Plugins → Desktop Lyrics → Configure*.

The zip installs these files:

| File / folder | Purpose |
|---|---|
| `mb_DesktopLyrics.dll` | The plugin |
| `RomajiCore.dll`, `LibNMeCab.dll` | Romaji engine and the Japanese text analyser it uses |
| `RomajiLyrics\IpaDic\` | Japanese dictionary for the romaji engine (≈ 50 MB) |
| `Newtonsoft.Json.dll`, `Cyotek.Windows.Forms.ColorPicker.dll` | Settings file and colour picker |
| `DesktopLyrics\` | Readme, licence and third-party notices |

If you also use the Romaji Lyrics panel plugin, it shares `RomajiCore.dll`, the dictionary and the
romaji on/off setting with this plugin.

## Updates

About 20 seconds after MusicBee starts, and every 12 hours after that, the plugin asks the GitHub
API for the latest release of this repository. If there is a newer version, a window shows the
release notes and these options:

- **Update now**: downloads the release zip and checks it against the SHA-256 file published with
  the release. Then it copies the files into your Plugins folder. If anything fails, all changes are
  undone. Restart MusicBee to start using the new version.
- **Release page**: opens the release on GitHub.
- **Skip this version**: you are not asked about this version again.
- **Later**: you are asked again the next time MusicBee starts.

In the settings you can turn the check off, or let the plugin install updates without asking first.
You can also check by hand at any time with *Check for updates...* in the right-click menu.

The plugin only contacts `api.github.com` and GitHub's download servers. It sends no information
about you or your library. MusicBee keeps loaded DLLs locked while it runs, so the updater renames
them to `*.updatebak` and puts the new files in their place. The leftover `*.updatebak` files are
deleted the next time MusicBee starts.

## Uninstall

Close MusicBee and delete `mb_DesktopLyrics.dll` and the `DesktopLyrics` folder from the Plugins
folder. If no other plugin needs them, you can also delete `RomajiCore.dll`, `LibNMeCab.dll`, the
`RomajiLyrics` folder, `Newtonsoft.Json.dll` and `Cyotek.Windows.Forms.ColorPicker.dll`. Romaji
Lyrics uses the first three, and many plugins use `Newtonsoft.Json.dll`. If you uninstall from
MusicBee's preferences instead, the plugin's settings files are removed too.

## Building

Requirements: the .NET SDK (any recent version) and the .NET Framework 4.8 targeting pack.

```powershell
./build.ps1    # creates dist\MusicBee-DesktopLyrics-Romaji-<version>.zip and its .sha256
```

Everything comes from this repository and NuGet:

- `src/DesktopLyrics`: the plugin (the original Desktop Lyrics source with the fork's changes)
- `external/RomajiCore`: the romaji engine (MIT)
- `LibNMeCab` and `LibNMeCab.IpaDicBin` from NuGet: the Japanese text analyser and dictionary

To publish a release, bump `AssemblyVersion` in `src/DesktopLyrics/Properties/AssemblyInfo.cs`, run
`build.ps1`, and attach both the zip and its `.sha256` to a GitHub release tagged `vX.Y.Z`. The
updater needs both files.

## Credits and licence

- **Desktop Lyrics** © Charlie Jiang ([cqjjjzr](https://github.com/cqjjjzr)), Apache License 2.0.
  This fork is a modified version of it. Every changed source file says at the top what was changed,
  as the licence requires.
- Fork changes © 2026 nishizumi-maho, also under the Apache License 2.0.
- RomajiCore © 2026 nishizumi-maho, MIT.
- NMeCab (LGPL-2.1), IPADIC (NAIST licence), Newtonsoft.Json (MIT), Cyotek ColorPicker (MIT). See
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- MusicBee and its plugin API are by Steven Mayall.

See [LICENSE](LICENSE) and [NOTICE](NOTICE).
