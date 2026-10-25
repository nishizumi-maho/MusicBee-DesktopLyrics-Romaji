using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class WordTimelineTests
    {
        // "言葉" 1000-1400, "が" 1400-1600, "人" 1800-2000 (a pause before 人)
        private static readonly List<LyricParser.WordSegment> Segs = new[]
        {
            ("言葉", 1000.0, 1400.0), ("が", 1400.0, 1600.0), ("人", 1800.0, 2000.0)
        }.Select(x => new LyricParser.WordSegment { Text = x.Item1, StartMs = x.Item2, EndMs = x.Item3 }).ToList();

        [Theory]
        [InlineData(0, 0)]        // before the line
        [InlineData(1000, 0)]     // first segment just starting
        [InlineData(1200, 1)]     // half of 言葉 (2 chars)
        [InlineData(1500, 2.5)]   // 言葉 done, half of が
        [InlineData(1700, 3)]     // in the pause: が done, 人 not started
        [InlineData(1900, 3.5)]
        [InlineData(5000, 4)]     // after the end: whole line
        public void LitCharsFillsGradually(double t, double expected)
        {
            Assert.Equal(expected, WordTimeline.LitChars(Segs, t), 3);
        }

        [Fact]
        public void ZeroLengthSegmentLightsAtOnce()
        {
            var s = new List<LyricParser.WordSegment> { new LyricParser.WordSegment { Text = "ab", StartMs = 100, EndMs = 100 } };
            Assert.Equal(0, WordTimeline.LitChars(s, 99), 3);
            Assert.Equal(2, WordTimeline.LitChars(s, 100), 3);
        }

        [Fact]
        public void PausedClockIsFrozen()
        {
            var t = 0.0;
            var clock = new PlaybackClock(() => t);
            clock.Sync(5000, playing: false);
            t = 300;
            Assert.Equal(5000, clock.NowMs(), 3);
        }

        [Fact]
        public void PlayingClockAdvancesBetweenSyncs()
        {
            var t = 0.0;
            var clock = new PlaybackClock(() => t);
            clock.Sync(5000, playing: true);
            t = 80;
            Assert.Equal(5080, clock.NowMs(), 3);
        }

        [Fact]
        public void SeekSnapsAndSmallDriftIsAdopted()
        {
            var t = 0.0;
            var clock = new PlaybackClock(() => t);
            clock.Sync(5000, playing: true);
            t = 100;
            clock.Sync(5130, playing: true);     // drift of 30 ms: adopt the reported position
            Assert.Equal(5130, clock.NowMs(), 3);
            clock.Sync(60000, playing: true);    // seek forward
            Assert.Equal(60000, clock.NowMs(), 3);
            clock.Sync(1000, playing: true);     // seek backwards
            Assert.Equal(1000, clock.NowMs(), 3);
        }
    }
}
