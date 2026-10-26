# Changelog (romaji fork)

All versions are based on Desktop Lyrics 1.10.1 by Charlie Jiang
(<https://github.com/cqjjjzr/MusicBee-DesktopLyrics>).

## 1.15.0 — 2026-10-26

- New: **Lyrics Panel (BETA)** (View > Lyrics Panel (BETA)). A floating panel in the style of
  MiniLyrics: borderless with rounded corners and a shadow, always on top and slightly translucent.
  The whole song scrolls through it, with the current line lit word by word and the other lines
  dimmed and fading at the edges. Hover it for a header with the song, smaller/bigger text,
  opacity and close. Drag anywhere to move it and drag an edge to resize it. It reopens with
  MusicBee until you close it.
- New: word-by-word highlight for enhanced LRC files (`<mm:ss.xx>` word timing), with its own
  colour. Romaji follows the same timing.
- New: karaoke window (View > Desktop Lyrics Karaoke) with the whole song, fullscreen with F11.
- New: continuous mode (two lines rolling up), an optional fixed box size per song, one continuous
  box behind sliding lines, and a move tab for dragging the lyrics.
- New: *Play the original lyric* setting. It plays `<song>.original.lrc` (the official lyric kept by
  LYRICS BOCA) instead of the enhanced `.lrc` when that file has timing.
- The lyrics clear during instrumental gaps instead of leaving the last line up.
- Word-timed lines arrive early by the transition time, so the first word is not late.
- Fixed: dragging the lyrics window no longer snaps it back to where it was a frame earlier.

## 1.14.1 — 2026-10-18

- The update window now shows the release notes as plain text. It used to show the raw Markdown
  (`###`, `**`, image links).
- After an automatic install, the update window now says which version was installed. It used to
  still say "is available" and list the old version.

## 1.14.0 — 2026-10-18

First public release of the fork.

- New: update notifications. About 20 seconds after MusicBee starts, and every 12 hours after that,
  the plugin checks this repository's latest GitHub release. If a newer version is out, it shows the
  release notes with *Update now*, *Release page*, *Skip this version* and *Later*. *Update now*
  downloads the release zip, checks it against the published SHA-256, and installs it into the
  Plugins folder. If anything fails, every replaced file is put back. Restart MusicBee to apply.
  Loaded DLLs are renamed to `*.updatebak`, and those leftovers are deleted on the next start.
- New settings: *Check GitHub for new versions* (on by default) and *Install them without asking*
  (off by default). There is also a *Check now* button. The right-click menu gets *Check for
  updates...*.
- New: *Show romaji (Japanese lyrics)* in the lyrics right-click menu and in the settings. Earlier
  versions only followed the Romaji Lyrics plugin's setting, so without that plugin there was no way
  to turn romaji off. The setting is still shared with that plugin when both are installed.
- Fix: turning the lyrics off with *View → Desktop Lyrics* disposed the window but kept using it.
  The lyrics timer then hit the disposed window every 100 ms. The animation clock now also stops when
  the window is destroyed.
- The build no longer copies files from a local MusicBee install. `LibNMeCab.dll` and the IPAdic
  dictionary come from NuGet (`LibNMeCab` 0.10.2, `LibNMeCab.IpaDicBin` 0.10.0), so anyone can build
  the release zip from this repository alone. The packaged files are byte-identical to earlier
  releases.
- Docs: removed the claim that only 32-bit MusicBee is supported. `LibNMeCab.dll` is an AnyCPU
  .NET Standard library.
- Added `THIRD-PARTY-NOTICES.md` and the NMeCab and IPADIC licence texts to the repository and the
  release zip.

## 1.13.0 — 2026-10-18

Based on Desktop Lyrics 1.10.1 by Charlie Jiang.

- New setting: animation frame rate for the smooth-scroll transitions, from 30 fps up to the primary
  monitor's refresh rate (default 60). Animating faster than the display refreshes only burns CPU, so
  the slider stops at the rate Windows reports for the monitor.
- The animation clock was replaced. It ran on `System.Windows.Forms.Timer`, which is built on
  `WM_TIMER` and is quantised to Windows' ~15.6 ms system tick. Measured on this machine: a request
  for 30 fps actually fired every 46.8 ms (~21 fps), every request above ~64 fps collapsed onto the
  same 15.6 ms quantum, and raising the tick rate with `timeBeginPeriod` did not lift it — the
  setting would have been meaningless above 64. The clock is now a winmm multimedia timer, measured
  delivering 30.0 / 59.0 / 122.0 / 245.0 frames per second for the 30 / 60 / 120 / 240 settings.
  Transitions that existed before this release also became smoother as a side effect.
- That timer fires on a worker thread, so frames are marshalled to the UI thread and coalesced: while
  a frame is queued, further callbacks are dropped, which lets a busy host thread drain its backlog
  instead of accumulating one.
- If the multimedia timer cannot be created, the animation falls back to the WinForms timer, so it
  degrades to coarse rather than freezing.

## 1.12.0 — 2026-10-18

Based on Desktop Lyrics 1.10.1 by Charlie Jiang.

- New: smooth line transitions. Lines no longer hard-cut; the outgoing line slides up and out while
  the new one rises into place, easing out like a sheet of paper coming to rest. The main line and
  the romaji/translation move together as one block. On by default; switching it off restores the
  old instant cut.
- New setting: transition time (200–1500 ms, default 500 ms).
- Only the frames actually needed are rendered: each line is rasterised once and cached, and a
  transition just translates and recomposites those two cached bitmaps, so text is never re-rendered
  while the animation runs. The timer also stops the moment a transition ends.
- Interrupting a transition mid-flight (fast-forwarding, or lyrics arriving sooner than expected)
  hands over from whatever is on screen at that instant, so the lyrics never jump back to their rest
  position.
- Fix: clearing the lyrics could leave the previous second line behind, so it reappeared the next time
  the settings were opened.

## 1.11.1 — 2026-10-18

Based on Desktop Lyrics 1.10.1 by Charlie Jiang.

- Fix: the lyrics window appeared to "not open at all". MusicBee frequently provides lyrics as plain
  untimed text; the parser rejected anything without `[mm:ss]` time tags, so the layered window was
  left fully transparent. Untimed lines are now shown one every 3 seconds. Timed LRC files are
  unaffected.
- Fix: with no lyrics to draw, the window now hides itself instead of sitting invisibly on the
  desktop.
- Fix: the settings dialog could silently drop saved values. All options were loaded inside a single
  `try`/`catch`, so one out-of-range value stopped the rest from being applied and closing the dialog
  wrote the control defaults back over the user's settings (this is how "hide lyrics when no lyrics
  available" got force-enabled). Each option is now loaded and clamped on its own.
- Fix: the gradient type was reset to "no gradient" every time the settings dialog was constructed.
- Fix: the plugin could fail to start, silently, when `Application.SetCompatibleTextRenderingDefault`
  threw or when MusicBee's window handle did not belong to a WinForms form.
- Fix: `PreserveSlash` and "next line when no translation" are now applied at startup, not only after
  the settings dialog has been opened once.
- Settings loaded from disk are now null-checked before use.
- The release package is now self-contained: it bundles the romaji engine (`RomajiCore.dll`,
  `LibNMeCab.dll`) and the IPAdic dictionary, so the Romaji Lyrics plugin is no longer required.
  Romaji stays switchable from the lyrics right-click menu.

## 1.11.0 — 2026-10-18

Based on Desktop Lyrics 1.10.1 by Charlie Jiang.

- Romaji of Japanese lyrics on the second line, when the Romaji Lyrics plugin is installed and its
  romaji option is on. Falls back to normal behaviour when it isn't installed.
- New setting: lyrics opacity (10–100 %).
- New setting: own colour for the second line.
- New setting: single padded box background behind all lines (uses the background opacity).
- Right-click menu on the lyrics: settings and opacity presets.
- Settings dialog grows to fit its options.
- Dependencies ship as separate DLLs instead of being embedded with Costura, which avoids antivirus
  false positives.
- Project converted to an SDK-style .NET Framework 4.8 project.
