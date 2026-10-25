using System.Collections.Generic;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class KaraokeLayoutTests
    {
        private static readonly List<LyricParser.LyricEntry> Entries = new List<LyricParser.LyricEntry>
        {
            new LyricParser.LyricEntry(1000, "a", null),
            new LyricParser.LyricEntry(5000, "b", null),
            new LyricParser.LyricEntry(9000, "c", null),
        };

        [Theory]
        [InlineData(0, -1)]      // intro: no line is being sung yet
        [InlineData(1000, 0)]
        [InlineData(6000, 1)]
        [InlineData(99999, 2)]
        public void CurrentIndex(double t, int expected)
        {
            Assert.Equal(expected, KaraokeLayout.CurrentIndex(Entries, t));
        }

        [Fact]
        public void ScrollTargetCentresTheCurrentLine()
        {
            var tops = new float[] { 0, 100, 250 };
            var heights = new float[] { 100, 150, 100 };
            // line 1 spans 100..250, its centre is 175; a 600 px view puts it at y = 300
            Assert.Equal(175 - 300, KaraokeLayout.ScrollTarget(tops, heights, 1, 600));
            // before the first line, the first line sits in the centre
            Assert.Equal(50 - 300, KaraokeLayout.ScrollTarget(tops, heights, -1, 600));
        }

        [Fact]
        public void EaseApproachesWithoutOvershootAndSnapsWhenClose()
        {
            var s = KaraokeLayout.Ease(0, 100, 16);
            Assert.True(s > 0 && s < 100);
            Assert.Equal(100, KaraokeLayout.Ease(99.8f, 100, 16));
            Assert.Equal(100, KaraokeLayout.Ease(0, 100, 5000));
            Assert.True(KaraokeLayout.Ease(100, 0, 16) < 100);
        }
    }
}
