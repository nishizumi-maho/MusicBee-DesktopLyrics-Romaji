// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): karaoke window.
using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    /// <summary>Which line the karaoke window is on and where it scrolls to.</summary>
    public static class KaraokeLayout
    {
        /// <summary>Index of the line being sung at <paramref name="timeMs"/>, or -1 before the first line.</summary>
        public static int CurrentIndex(IList<LyricParser.LyricEntry> entries, double timeMs)
        {
            if (entries.Count == 0 || timeMs < entries[0].TimeMs) return -1;
            return LyricsController.SelectEntryIndex(entries, timeMs, 0);
        }

        /// <summary>Scroll offset that puts the middle of line <paramref name="index"/> in the middle of the
        /// view (the first line when nothing is being sung yet).</summary>
        public static float ScrollTarget(float[] lineTops, float[] lineHeights, int index, float viewHeight)
        {
            if (lineTops.Length == 0) return -viewHeight / 2;
            var i = Math.Max(0, Math.Min(index, lineTops.Length - 1));
            return lineTops[i] + lineHeights[i] / 2 - viewHeight / 2;
        }

        /// <summary>Exponential approach of the scroll position towards its target: quick at first,
        /// settling gently, frame-rate independent; snaps once less than a pixel away.</summary>
        public static float Ease(float current, float target, double elapsedMs)
        {
            var k = 1 - Math.Exp(-elapsedMs / 90.0);
            var next = current + (float)((target - current) * k);
            return Math.Abs(target - next) < 0.5f ? target : next;
        }
    }
}
