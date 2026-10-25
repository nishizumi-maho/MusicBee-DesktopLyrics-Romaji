using System.Collections.Generic;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class SongViewsTests
    {
        private static LyricParser.Lyrics Lyrics(bool translation) => new LyricParser.Lyrics
        {
            HasTranslation = translation,
            Entries = new List<LyricParser.LyricEntry>
            {
                new LyricParser.LyricEntry(0, "a", translation ? "a2" : null),
                new LyricParser.LyricEntry(1000, "b", translation ? "b2" : null),
            }
        };

        [Fact]
        public void TranslationOrRomajiPairsEachLineWithItsSecondLine()
        {
            Assert.Equal(new[] { ("a", "a2"), ("b", "b2") }, LyricsController.SongViews(Lyrics(true), nextLineWhenNoTranslation: true));
        }

        [Fact]
        public void NextLineModePairsEachLineWithTheNextOne()
        {
            Assert.Equal(new[] { ("a", "b"), ("b", (string)null) }, LyricsController.SongViews(Lyrics(false), nextLineWhenNoTranslation: true));
        }

        [Fact]
        public void SingleLineMode()
        {
            Assert.Equal(new[] { ("a", (string)null), ("b", (string)null) }, LyricsController.SongViews(Lyrics(false), nextLineWhenNoTranslation: false));
        }
    }
}
