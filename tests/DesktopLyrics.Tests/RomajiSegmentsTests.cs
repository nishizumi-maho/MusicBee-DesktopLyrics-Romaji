using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class RomajiSegmentsTests
    {
        private static readonly Dictionary<string, string> Fake = new Dictionary<string, string>
        {
            ["言葉"] = "kotoba", ["が"] = "ga", ["、"] = "", ["人"] = "hito"
        };

        private static List<LyricParser.WordSegment> Segs(params (string text, double start, double end)[] s) =>
            s.Select(x => new LyricParser.WordSegment { Text = x.text, StartMs = x.start, EndMs = x.end }).ToList();

        [Fact]
        public void EachPieceKeepsItsSegmentTimes()
        {
            var r = LyricsController.BuildRomajiSegments(Segs(("言葉", 100, 400), ("が", 400, 600)), t => Fake[t]);
            Assert.Equal(new[] { "kotoba ", "ga" }, r.Select(s => s.Text));
            Assert.Equal(new double[] { 100, 400 }, r.Select(s => s.StartMs));
            Assert.Equal(new double[] { 400, 600 }, r.Select(s => s.EndMs));
        }

        [Fact]
        public void EmptyPiecesAreSkippedWithoutBreakingTheOthers()
        {
            var r = LyricsController.BuildRomajiSegments(Segs(("言葉", 0, 300), ("、", 300, 350), ("人", 350, 700)), t => Fake[t]);
            Assert.Equal("kotoba hito", string.Concat(r.Select(s => s.Text)));
            Assert.Equal(350, r[1].StartMs);
        }
    }
}
