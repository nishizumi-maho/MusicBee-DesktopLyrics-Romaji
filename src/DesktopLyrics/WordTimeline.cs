// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): word-by-word highlight timing.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MusicBeePlugin
{
    public static class WordTimeline
    {
        /// <summary>How many characters of a word-timed line are lit at <paramref name="timeMs"/>:
        /// whole segments already sung, plus the sung fraction of the segment in progress. Fractional,
        /// so the fill can move smoothly inside a word.</summary>
        public static double LitChars(IReadOnlyList<LyricParser.WordSegment> segments, double timeMs)
        {
            double lit = 0;
            foreach (var s in segments)
            {
                if (timeMs < s.StartMs) break;
                var len = s.Text.Length;
                if (timeMs >= s.EndMs) { lit += len; continue; }
                lit += len * (timeMs - s.StartMs) / (s.EndMs - s.StartMs);
                break;
            }
            return lit;
        }
    }

    /// <summary>Playback position between MusicBee's position polls (every 100 ms), so the highlight
    /// can move every frame. Frozen while paused; a reported position ahead of the estimate, or more
    /// than <see cref="SeekThresholdMs"/> behind it (a seek), is taken as is. A report slightly behind
    /// the estimate is ignored, so the fill never creeps backwards.</summary>
    public class PlaybackClock
    {
        public const double SeekThresholdMs = 250;

        private readonly Func<double> _nowMs;
        private double _basePos, _baseAt;
        private bool _playing;

        public PlaybackClock() : this(StopwatchNow()) { }

        public PlaybackClock(Func<double> nowMs) { _nowMs = nowMs; }

        public bool Playing => _playing;

        public double NowMs() => _playing ? _basePos + (_nowMs() - _baseAt) : _basePos;

        public void Sync(double positionMs, bool playing)
        {
            var estimate = NowMs();
            if (!playing || !_playing || positionMs >= estimate || estimate - positionMs > SeekThresholdMs)
                _basePos = positionMs;
            else
                _basePos = estimate;
            _baseAt = _nowMs();
            _playing = playing;
        }

        private static Func<double> StopwatchNow()
        {
            var sw = Stopwatch.StartNew();
            return () => sw.Elapsed.TotalMilliseconds;
        }
    }
}
