# Releasing a new version

The plugin's updater reads the **latest** GitHub release of
`nishizumi-maho/MusicBee-DesktopLyrics-Romaji`. To work, each release needs:

- a tag named `vX.Y.Z` that matches `AssemblyVersion` in `src/DesktopLyrics/Properties/AssemblyInfo.cs`
- a zip asset named `MusicBee-DesktopLyrics-Romaji-X.Y.Z.zip`
- its checksum asset, `MusicBee-DesktopLyrics-Romaji-X.Y.Z.zip.sha256`

Drafts and pre-releases are ignored, so you can use a pre-release to test a build before users get it.

## Steps

1. Bump `AssemblyVersion` and `AssemblyFileVersion` in `src/DesktopLyrics/Properties/AssemblyInfo.cs`.
2. Add a section to `CHANGELOG.md`.
3. Build:
   ```powershell
   ./build.ps1
   ```
4. Commit, tag and push:
   ```powershell
   git commit -am "Release X.Y.Z"
   git tag vX.Y.Z
   git push origin main --tags
   ```
5. Create the release, using that version's changelog section as the notes:
   ```powershell
   gh release create vX.Y.Z dist/MusicBee-DesktopLyrics-Romaji-X.Y.Z.zip dist/MusicBee-DesktopLyrics-Romaji-X.Y.Z.zip.sha256 --title "Desktop Lyrics (romaji fork) X.Y.Z" --notes-file notes.md
   ```
6. Installed copies with update checks on will offer the update within 12 hours, or on the next
   MusicBee start.

## Credit

This is a fork of [Desktop Lyrics](https://github.com/cqjjjzr/MusicBee-DesktopLyrics) by Charlie
Jiang. Keep the original credit in the README, NOTICE, forum posts and add-on listing. Keep the
change notice at the top of every source file you modify, as Apache License 2.0 section 4(b)
requires.

## MusicBee add-on listing (optional)

The original Desktop Lyrics has its own add-on page
(<https://www.getmusicbee.com/addons/plugins/171/desktop-lyrics/>). If you list the fork as well,
make clear it is a fork:

| Field | Value |
|---|---|
| Add-on type | Plugins |
| Title | Desktop Lyrics (romaji fork) |
| Short description | Fork of Desktop Lyrics by CharlieJiang: romaji under Japanese lyrics (engine and dictionary bundled), smooth line transitions, lyrics opacity, second-line colour, a single box background and update notifications. |
| Minimum MusicBee version | 3.6 |
| Tags | lyrics, desktop lyrics, romaji, japanese, synced lyrics, overlay |
| Screenshots | `docs/screenshots/desktop-lyrics-box.png`, `docs/screenshots/desktop-lyrics-settings.png` |
| Download link | <https://github.com/nishizumi-maho/MusicBee-DesktopLyrics-Romaji/releases/latest> |
