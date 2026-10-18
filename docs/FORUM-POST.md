# MusicBee forum announcement

Post it as a new topic in **Customizations → Plugins** on <https://getmusicbee.com/forum/>. The
forum uses BBCode, so paste the block below as-is. Attach or link
`docs/screenshots/desktop-lyrics-box.png` where the `[img]` tag is. The forum may not show images
hosted on GitHub; if so, upload the image to the post instead.

It would also be polite to let CharlieJiang know, by a reply in the original Desktop Lyrics topic,
a forum PM, or an issue on <https://github.com/cqjjjzr/MusicBee-DesktopLyrics>.

**Subject:** `Desktop Lyrics (romaji fork) 1.14.0 – romaji for Japanese lyrics, smooth transitions`

```bbcode
[size=14pt][b]Desktop Lyrics – romaji fork[/b][/size]
A fork of [url=https://www.getmusicbee.com/addons/plugins/171/desktop-lyrics/]Desktop Lyrics[/url] by [b]CharlieJiang[/b] ([url=https://github.com/cqjjjzr/MusicBee-DesktopLyrics]source[/url]). All credit for the original plugin goes to its author. This fork builds on version 1.10.1 and adds a few things I wanted for Japanese music.

[img]https://raw.githubusercontent.com/nishizumi-maho/MusicBee-DesktopLyrics-Romaji/main/docs/screenshots/desktop-lyrics-box.png[/img]

[b]What's new compared with the original[/b]
[list]
[li][b]Romaji under Japanese lyrics.[/b] The converter and its dictionary are included, so there's nothing else to install. Turn it on or off from the lyrics right-click menu.[/li]
[li][b]Smooth line transitions.[/b] The old line slides up while the new one rises into place, instead of a hard cut. Duration is adjustable (200–1500 ms) or you can switch it off. The frame rate goes up to your monitor's refresh rate.[/li]
[li][b]Lyrics opacity[/b] (10–100 %), with quick presets in the right-click menu.[/li]
[li][b]Separate colour for the second line[/b] (romaji or translation).[/li]
[li][b]Single box background[/b] behind both lines.[/li]
[li][b]Plain, untimed lyrics are shown too[/b] (one line every 3 s), not only synced LRC.[/li]
[li][b]Update notifications.[/b] The plugin checks GitHub for new releases and asks before installing (checksum-verified). You can turn this off in the settings.[/li]
[li]No DLLs embedded in the plugin, which avoids the antivirus false positives some people got with the original build. Plus a few bug fixes.[/li]
[/list]

[b]Download[/b]
[url=https://github.com/nishizumi-maho/MusicBee-DesktopLyrics-Romaji/releases/latest]GitHub releases[/url] – source code on the same page (Apache 2.0, like the original).

[b]Install[/b]
[list type=decimal]
[li]Close MusicBee.[/li]
[li]Extract [i]everything[/i] in the zip into your Plugins folder, keeping the folders ([tt]%APPDATA%\MusicBee\Plugins[/tt] for the installer version, [tt]<MusicBee folder>\Plugins[/tt] for portable). Replace the old Desktop Lyrics files if asked.[/li]
[li]Start MusicBee. Your existing Desktop Lyrics settings are kept. Toggle the lyrics with [i]View → Desktop Lyrics[/i] and right-click them for options.[/li]
[/list]

[b]Notes[/b]
[list]
[li]It replaces the original Desktop Lyrics ([tt]mb_DesktopLyrics.dll[/tt]). Don't run both at once.[/li]
[li]The update check only contacts api.github.com and GitHub's download servers. Nothing about you or your library is sent.[/li]
[/list]

Feedback and bug reports are welcome here or on [url=https://github.com/nishizumi-maho/MusicBee-DesktopLyrics-Romaji/issues]GitHub issues[/url].
```
