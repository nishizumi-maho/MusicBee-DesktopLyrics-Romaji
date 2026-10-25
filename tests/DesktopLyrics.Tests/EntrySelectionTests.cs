using System.Collections.Generic;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class EntrySelectionTests
    {
        private static LyricParser.LyricEntry Word(double start, double lastEnd) =>
            new LyricParser.LyricEntry(start, "x", null)
            {
                Segments = new List<LyricParser.WordSegment>
                {
                    new LyricParser.WordSegment { Text = "x", StartMs = start, EndMs = lastEnd }
                }
            };

        private static LyricParser.LyricEntry Plain(double start) => new LyricParser.LyricEntry(start, "x", null);

        [Fact]
        public void PlainLyricsSwitchExactlyAtTheLineTime()
        {
            var e = new List<LyricParser.LyricEntry> { Plain(1000), Plain(5000) };
            Assert.Equal(0, LyricsController.SelectEntryIndex(e, 4999, 500));
            Assert.Equal(1, LyricsController.SelectEntryIndex(e, 5000, 500));
        }

        [Fact]
        public void WordTimedLineArrivesEarlyByTheLeadIn()
        {
            // previous line's last word ends at 4000, next line sung at 5000, lead-in 500
            var e = new List<LyricParser.LyricEntry> { Word(1000, 4000), Word(5000, 6000) };
            Assert.Equal(0, LyricsController.SelectEntryIndex(e, 4499, 500));
            Assert.Equal(1, LyricsController.SelectEntryIndex(e, 4500, 500));
        }

        [Fact]
        public void LeadInNeverCutsThePreviousLinesLastWord()
        {
            // the previous line is still being sung until 4900: wait for it
            var e = new List<LyricParser.LyricEntry> { Word(1000, 4900), Word(5000, 6000) };
            Assert.Equal(0, LyricsController.SelectEntryIndex(e, 4800, 500));
            Assert.Equal(1, LyricsController.SelectEntryIndex(e, 4900, 500));
        }

        [Fact]
        public void BeforeTheFirstLineShowsTheFirstLine()
        {
            var e = new List<LyricParser.LyricEntry> { Word(1000, 2000), Word(5000, 6000) };
            Assert.Equal(0, LyricsController.SelectEntryIndex(e, 0, 500));
        }
    }
}
