// Modified by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): Shows romaji (bundled RomajiCore engine) as the second line of Japanese lyrics, with an on/off switch.
// Original work: Copyright Charlie Jiang, licensed under the Apache License 2.0.
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RomajiCore;

namespace MusicBeePlugin
{
    public class LyricsController
    {
        public bool NextLineWhenNoTranslation { get; set; }

        private readonly Plugin.MusicBeeApiInterface _interface;
        private string _lastLyrics;
        private bool _lastRomaji;
        private LyricParser.Lyrics _lyrics;
        public LyricsController(Plugin.MusicBeeApiInterface @interface)
        {
            _interface = @interface;
        }

        public LyricView UpdateLyrics(bool useGeneratedWhenUnavailable)
        {
            // TODO passively change?
            // MusicBee returns an empty (or null) HasLyrics tag when the field is simply unknown,
            // while NowPlaying_GetLyrics() may still hold usable text, so treat both as "try anyway".
            var hasLyrics = _interface.NowPlaying_GetFileTag(Plugin.MetaDataType.HasLyrics) ?? "";
            if (hasLyrics.StartsWith("Y")  || hasLyrics.Length == 0)
            {
                var lyrics = _interface.NowPlaying_GetLyrics();
                var romajiOn = RomajiAvailable && RomajiEnabled();
                if (lyrics != _lastLyrics || romajiOn != _lastRomaji)
                {
                    _lyrics = LyricParser.ParseLyric(lyrics);
                    if (romajiOn) TryApplyRomaji(_lyrics);
                    _lastLyrics = lyrics;
                    _lastRomaji = romajiOn;
                }
            }
            else
            {
                _lyrics = null;
                _lastLyrics = null;
            }
            

            if (_lyrics == null)
            {
                if (useGeneratedWhenUnavailable)
                    return new LyricView(
                    _interface.NowPlaying_GetFileTag(Plugin.MetaDataType.TrackTitle) + " - " +
                    _interface.NowPlaying_GetFileTag(Plugin.MetaDataType.Artist), null);
                return null;
            }

            var time = _interface.Player_GetPosition();
            var nTime = time + _lyrics.Offset;
            var entries = _lyrics.Entries;

            LyricParser.LyricEntry currentEntry = null;
            LyricParser.LyricEntry nextEntry = null;

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].TimeMs > nTime && i > 0)
                {
                    currentEntry = entries[i - 1];
                    nextEntry = entries[i];
                    break;
                }
            }

            if (currentEntry == null)
            {
                if (entries.Count <= 0) return null;
                currentEntry = entries[entries.Count - 1];
            }

            if (_lyrics.HasTranslation || nextEntry == null || !NextLineWhenNoTranslation)
                return new LyricView(currentEntry.LyricLine1, currentEntry.LyricLine2);
            return new LyricView(currentEntry.LyricLine1, nextEntry.LyricLine1);
        }

        // Romaji support comes from RomajiCore.dll, which ships next to this plugin. If someone deletes it
        // the RomajiCore types can't load, so every use stays behind these non-inlined guards.
        public static bool RomajiAvailable = true;

        // The guards themselves must not mention RomajiCore types: a missing assembly fails while a
        // method is being compiled, before its try block runs.
        private bool RomajiEnabled()
        {
            try { return ReadRomajiSetting(_interface.Setting_GetPersistentStoragePath()); }
            catch (Exception e) when (e is FileNotFoundException || e is FileLoadException || e is TypeLoadException)
            {
                RomajiAvailable = false;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadRomajiSetting(string storagePath) => RomajiSettings.Get(storagePath).RomajiEnabled;

        /// <summary>The romaji switch, or null when the romaji engine is not available. The setting is
        /// shared with the Romaji Lyrics plugin, if that is installed too.</summary>
        public bool? GetRomajiSetting()
        {
            if (!RomajiAvailable) return null;
            var on = RomajiEnabled();
            return RomajiAvailable ? on : (bool?)null;
        }

        public void SetRomajiSetting(bool on)
        {
            if (!RomajiAvailable) return;
            try { WriteRomajiSetting(_interface.Setting_GetPersistentStoragePath(), on); }
            catch (Exception e) when (e is FileNotFoundException || e is FileLoadException || e is TypeLoadException)
            {
                RomajiAvailable = false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WriteRomajiSetting(string storagePath, bool on)
        {
            var settings = RomajiSettings.Get(storagePath);
            if (settings.RomajiEnabled == on) return;
            settings.RomajiEnabled = on;
            settings.Save();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TryApplyRomaji(LyricParser.Lyrics lyrics)
        {
            try { ApplyRomaji(lyrics); }
            catch (Exception e) when (e is FileNotFoundException || e is FileLoadException || e is TypeLoadException)
            {
                RomajiAvailable = false;
            }
        }

        // Japanese lyrics: the second line becomes the romaji of the first (replacing a translation).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyRomaji(LyricParser.Lyrics lyrics)
        {
            if (lyrics == null || !Romanizer.IsJapaneseText(lyrics.Entries.Select(e => e.LyricLine1))) return;
            foreach (var entry in lyrics.Entries)
            {
                var romaji = Romanizer.ToRomaji(entry.LyricLine1);
                if (!string.IsNullOrEmpty(romaji)) entry.LyricLine2 = romaji;
            }
            lyrics.HasTranslation = true;
        }

        public class LyricView
        {
            // C# version < 8.0, can't use nullable reference feature....
            public string LyricLine1 { get; set; } // Nullable
            public string LyricLine2 { get; set; } // Nullable

            public LyricView(string lyricLine1, string lyricLine2)
            {
                LyricLine1 = lyricLine1;
                LyricLine2 = lyricLine2;
            }

            public override string ToString()
            {
                return $"[LyricView: {LyricLine1}, {LyricLine2}]";
            }
        }
    }
}
