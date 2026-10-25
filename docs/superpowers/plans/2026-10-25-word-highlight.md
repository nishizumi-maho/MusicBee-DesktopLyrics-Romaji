# Desktop Lyrics — Word-by-Word Highlight — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the playing song's lyrics are an enhanced `.lrc` (word timing), Desktop Lyrics fills each word gradually, left to right, while it is sung — on the main line and on the romaji line together.

**Architecture:** The parser turns `<mm:ss.xx>` tags into timed segments per line. A pure `WordTimeline` answers "how many characters of line 1 / line 2 are lit at time t" (fractional). The renderer draws each line twice (normal style and a text-only highlight layer) and reports character x-edges; `FrmLyrics` keeps the multimedia clock running while a word-timed line is on screen and, per frame, blits the highlight layer clipped at the lit x. Playback time is interpolated between the plugin's 100 ms position polls.

**Tech Stack:** C# / .NET Framework 4.8, WinForms + GDI+, xUnit (new test project), RomajiCore.

**Spec:** `H:\New folder (5)\LYRICS BOCA\docs\superpowers\specs\2026-10-25-lyrics-boca-design.md` — sections 5 (format) and 6 (plugin).

## Global Constraints

- Highlight off, or a song without word timing: behaviour and per-frame cost identical to today.
- Text between `<>` marks is shown exactly as written: no separators inserted (Japanese unspaced, Latin carries its own spaces).
- `[re:]` and `[#...]` lines are metadata; plain LRC parses exactly as before.
- New settings: `WordHighlight` (default true) and `HighlightColor` (default: a cyan, `#50E6F0`).
- Romaji line lights in sync: each Japanese segment maps to its own romaji piece.
- Never regress stability: no bitmap shared across threads; every HBITMAP/DC released each frame; memory of MusicBee stays flat during playback.

## Review Focus

1. Lyrics whose `<>` tags MusicBee strips or rewrites before handing them to the plugin — highlight must silently fall back to line mode (Task 1 checks what MusicBee really delivers).
2. Seek backwards/forwards and pause mid-word — the fill must jump/stop immediately, never animate backwards slowly (Task 3, Task 5).
3. Line longer than the window (clipped by NoWrap) — x-edges beyond the bitmap clamp to its width (Task 4).
4. Romaji conversion that returns empty for a segment (punctuation) — the romaji line keeps the other pieces' timing (Task 2).
5. Switching tracks during a smooth-scroll transition — no highlight drawn on the outgoing block, no exception (Task 5).

---

### Task 1: What MusicBee delivers, test project, parser

**Files:** Create `tests/DesktopLyrics.Tests/DesktopLyrics.Tests.csproj`, `tests/DesktopLyrics.Tests/LyricParserTests.cs`; Modify `src/DesktopLyrics/LyricParser.cs`, `MusicBee-DesktopLyrics-Romaji.sln`.

**Interfaces — Produces:**
- `LyricParser.WordSegment { string Text; double StartMs; double EndMs; }`
- `LyricParser.LyricEntry.Segments : List<WordSegment>` (null for line-only entries); `LyricEntry.Segments2 : List<WordSegment>` (romaji pieces, set by Task 2).
- `LyricParser.Lyrics.HasWordTimestamps : bool`

- [ ] Step 1: stash the uncommitted parser draft (`git stash push -m "other AI word-timing draft"`), keeping it recoverable.
- [ ] Step 2: verify what MusicBee hands the plugin: temporarily `MB_Trace` the first 300 chars of `NowPlaying_GetLyrics()` on lyrics change, build, install, play the Konomi Suzuki Reweave, read the trace. Expected: `<mm:ss.xx>` tags present. If stripped, stop and report (the design depends on it). Remove the trace.
- [ ] Step 3: test project (xunit, net48, references the plugin project). Tests: enhanced line → segments with start/end (end = next start / final mark); Japanese text concatenated without spaces; Latin keeps its own spaces; `[re:]`/`[#source:..]` skipped; plain LRC unchanged (`Segments == null`, same `TimeMs`/text as before); mixed file (some lines enhanced) → `HasWordTimestamps` true and line-only entries keep `Segments == null`.
- [ ] Step 4: run → FAIL; implement in `LyricParser.ParseLyric`; run → PASS; commit `feat: parse enhanced LRC word timing`.

### Task 2: Romaji per segment

**Files:** Modify `src/DesktopLyrics/LyricsController.cs` (`ApplyRomaji`); Test `tests/DesktopLyrics.Tests/RomajiSegmentsTests.cs`.

**Interfaces — Produces:** `LyricsController.BuildRomajiSegments(List<WordSegment> segs, Func<string,string> toRomaji) -> List<WordSegment>` (static, testable with a fake romanizer); `ApplyRomaji` sets `LyricLine2 = string.Join(" ", pieces)` and `Segments2` with the same times; empty pieces are skipped.

- [ ] Steps: test (fake `toRomaji`: 言葉→"kotoba", が→"ga", "、"→"") → FAIL → implement → PASS → commit `feat: romaji line follows word timing`.

### Task 3: WordTimeline

**Files:** Create `src/DesktopLyrics/WordTimeline.cs`; Test `tests/DesktopLyrics.Tests/WordTimelineTests.cs`.

**Interfaces — Produces:**
- `static double WordTimeline.LitChars(IReadOnlyList<WordSegment> segs, double timeMs)` — characters of all segments with `EndMs <= t`, plus `len * (t - Start) / (End - Start)` of the segment being sung; 0 before the first start; full length after the last end.
- `class PlaybackClock { void Sync(double positionMs, bool playing); double NowMs(); }` — interpolates with a `Stopwatch` while playing, frozen when paused; a sync that differs from the estimate by > 250 ms snaps (seek), smaller drifts are adopted directly.

- [ ] Steps: tests (before/inside/after segments, zero-length segment, paused clock frozen, seek snaps) → FAIL → implement → PASS → commit `feat: word timeline and playback clock`.

### Task 4: Renderer — highlight layer and character edges

**Files:** Modify `src/DesktopLyrics/LyricsRenderer.cs`.

**Interfaces — Produces:**
- `class RenderedBlock : IDisposable { Bitmap Base; Bitmap Highlight; RectangleF[] LineRects; float[][] CharEdges; }` — `Highlight` has only the text (highlight colour, same border/shadow, no background box); `CharEdges[line][i]` = x of the right edge of character i in block coordinates (clamped to the bitmap width).
- `LyricsRenderer.Render1LineBlock(string, IDeviceContext, bool withHighlight)` / `Render2LineBlock(string, string, IDeviceContext, bool withHighlight)` returning `RenderedBlock`. Existing `Render1LineLyrics`/`Render2LineLyrics` become thin wrappers (return `Base`), so the non-highlight path is unchanged.
- Edges: `Graphics.MeasureCharacterRanges` with the same `Format`, rect and pixel em size as the `GraphicsPath`, in batches of 32 ranges.

- [ ] Step 1: unit test in the test project: render "言葉が" and "eu vou" into a block; edges strictly increasing, last edge ≤ width, `Highlight` pixels outside text rows are fully transparent.
- [ ] Steps: FAIL → implement → PASS → commit `feat: renderer highlight layer and character edges`.

### Task 5: FrmLyrics — continuous highlight frames + stability fixes

**Files:** Modify `src/DesktopLyrics/FrmLyrics.cs`, `src/DesktopLyrics/DesktopLyrics.cs`, `src/DesktopLyrics/LyricsController.cs` (`LyricView` carries `Segments`, `Segments2`).

**Interfaces — Consumes:** Tasks 1–4. **Produces:** `FrmLyrics.UpdateLyrics(LyricView view)`; `FrmLyrics.SyncPlayback(double positionMs, bool playing)`.

- [ ] Step 1 (stability, first): `PresentFrame` and every block access only on the UI thread (assert `InvokeRequired == false`; marshal otherwise); `GdiplusHelper.SetBitmap` releases HBITMAP and DCs in `finally`. Verify by playing for 5 minutes in MusicBee: no GDI+ errors in `ErrorLog.dat`, private memory flat (±20 MB).
- [ ] Step 2: at rest with a word-timed block and `WordHighlight` on, keep the multimedia clock running; each tick: `t = clock.NowMs()`, compute lit x for line 1 and line 2, compose `Base` + `Highlight` clipped to `[0, x] × LineRect`, present. Stop the clock when the line is fully lit and nothing changes until the next line.
- [ ] Step 3: `DesktopLyrics.TimerTick` calls `SyncPlayback(Player_GetPosition(), Player_GetPlayState() == Playing)` every 100 ms; play-state changes sync immediately.
- [ ] Step 4: smooth-scroll transition shows `Base` only; highlight resumes at rest.
- [ ] Step 5: build; commit `feat: gradual word highlight in the lyrics window`.

### Task 6: Settings

**Files:** Modify `src/DesktopLyrics/FrmSettings.cs`, `FrmSettings.Designer.cs`, `SettingsObj` (in `FrmSettings.cs`).

- [ ] Checkbox "Highlight words as they are sung (word-timed lyrics)" and a colour button (Cyotek picker, like the existing colours); defaults per Global Constraints; old settings files load with the defaults. Commit `feat: word highlight settings`.

### Task 7: Install and verify in MusicBee

- [ ] Back up the installed `mb_DesktopLyrics.dll` (`.bak`), build with `build.ps1`, copy the new DLL into `%APPDATA%\MusicBee\Plugins`.
- [ ] Play the Miura Jam BR Reweave (Portuguese) and the Konomi Suzuki Reweave (Japanese, romaji on and off): fill follows the voice, romaji line in sync, pause/seek behave, a plain-LRC song looks exactly as before.
- [ ] 10 minutes of playback: MusicBee private memory flat, no new GDI+/OutOfMemory entries in `ErrorLog.dat`.
- [ ] Bump version in `Properties/AssemblyInfo.cs`, CHANGELOG entry; commit.
