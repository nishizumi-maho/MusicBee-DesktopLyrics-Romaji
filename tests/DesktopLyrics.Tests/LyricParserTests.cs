using System.Linq;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class LyricParserTests
    {
        private const string Enhanced =
            "[ti:Reweave]\n[re:LYRICS BOCA 1.0.0]\n[#source:netease conf:0.36]\n[#engine:1]\n" +
            "[00:23.11]<00:23.11>言葉<00:23.61>が<00:23.79>人<00:25.54>\n" +
            "[00:25.26]<00:25.26>eu <00:25.83>vou<00:26.43>\n" +
            "[00:30.00]linha sem palavras\n";

        [Fact]
        public void EnhancedLineHasSegmentsWithStartAndEnd()
        {
            var e = LyricParser.ParseLyric(Enhanced).Entries[0];
            Assert.Equal(23110, e.TimeMs);
            Assert.Equal(new[] { "言葉", "が", "人" }, e.Segments.Select(s => s.Text));
            Assert.Equal(new double[] { 23110, 23610, 23790 }, e.Segments.Select(s => s.StartMs));
            Assert.Equal(new double[] { 23610, 23790, 25540 }, e.Segments.Select(s => s.EndMs));
        }

        [Fact]
        public void JapaneseIsJoinedWithoutSpacesAndLatinKeepsItsOwn()
        {
            var entries = LyricParser.ParseLyric(Enhanced).Entries;
            Assert.Equal("言葉が人", entries[0].LyricLine1);
            Assert.Equal("eu vou", entries[1].LyricLine1);
        }

        [Fact]
        public void MetadataLinesAreNotLyrics()
        {
            var entries = LyricParser.ParseLyric(Enhanced).Entries;
            Assert.Equal(3, entries.Count);
            Assert.DoesNotContain(entries, e => e.LyricLine1.Contains("LYRICS BOCA") || e.LyricLine1.Contains("netease"));
        }

        [Fact]
        public void MixedFileKeepsLineOnlyEntries()
        {
            var lyrics = LyricParser.ParseLyric(Enhanced);
            Assert.True(lyrics.HasWordTimestamps);
            Assert.Null(lyrics.Entries[2].Segments);
            Assert.Equal("linha sem palavras", lyrics.Entries[2].LyricLine1);
        }

        [Fact]
        public void PlainLrcIsUnchanged()
        {
            var lyrics = LyricParser.ParseLyric("[00:01.00]a\n[00:02.50]b\n");
            Assert.False(lyrics.HasWordTimestamps);
            Assert.Equal(new double[] { 1000, 2500 }, lyrics.Entries.Select(e => e.TimeMs));
            Assert.Equal(new[] { "a", "b" }, lyrics.Entries.Select(e => e.LyricLine1));
            Assert.All(lyrics.Entries, e => Assert.Null(e.Segments));
        }

        [Fact]
        public void MillisecondTagsAreRead()
        {
            var e = LyricParser.ParseLyric("[00:01.000]<00:01.250>a<00:01.500>").Entries[0];
            Assert.Equal(1250, e.Segments[0].StartMs);
            Assert.Equal(1500, e.Segments[0].EndMs);
        }
    }
}
