using System.Collections.Generic;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class ContinuousViewTests
    {
        private static LyricParser.Lyrics Lyrics(bool translation) => new LyricParser.Lyrics
        {
            HasTranslation = translation,
            Entries = new List<LyricParser.LyricEntry>
            {
                new LyricParser.LyricEntry(0, "a", translation ? "a2" : null)
                {
                    Segments = new List<LyricParser.WordSegment> { new LyricParser.WordSegment { Text = "a", StartMs = 0, EndMs = 500 } }
                },
                new LyricParser.LyricEntry(1000, "", null),
                new LyricParser.LyricEntry(10000, "b", translation ? "b2" : null),
            }
        };

        [Fact]
        public void CurrentLineCarriesTheNextNonBlankLine()
        {
            var view = LyricsController.ContinuousView(Lyrics(true), 0, 100, 0);
            Assert.Equal(("a", "a2"), (view.LyricLine1, view.LyricLine2));
            Assert.Equal(("b", "b2"), (view.NextLine1, view.NextLine2));
        }

        [Fact]
        public void WithoutTranslationEachLineIsSingle()
        {
            var view = LyricsController.ContinuousView(Lyrics(false), 0, 100, 0);
            Assert.Null(view.LyricLine2);
            Assert.Equal("b", view.NextLine1);
            Assert.Null(view.NextLine2);
        }

        [Fact]
        public void InstrumentalGapShowsOnlyTheUpcomingLine()
        {
            // Line "a" ended at 500 ms; 3 s later is past the linger time.
            var lyrics = Lyrics(true);
            lyrics.Entries.RemoveAt(1);
            var view = LyricsController.ContinuousView(lyrics, 0, 3000, 0);
            Assert.Equal("", view.LyricLine1);
            Assert.Equal("b", view.NextLine1);
        }

        [Fact]
        public void LastLineHasNoNext()
        {
            var view = LyricsController.ContinuousView(Lyrics(true), 2, 10100, 0);
            Assert.Equal("b", view.LyricLine1);
            Assert.Null(view.NextLine1);
        }
    }
}
