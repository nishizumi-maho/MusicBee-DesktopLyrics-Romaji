using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class OriginalLyricsTests
    {
        [Fact]
        public void OriginalLyricSitsNextToTheSongWithItsOwnName()
        {
            Assert.Equal(@"F:\M\01 - Hello.original.lrc", LyricsController.OriginalLyricsPath(@"F:\M\01 - Hello.flac"));
        }

        [Fact]
        public void TimedOriginalIsUsable()
        {
            Assert.NotNull(LyricsController.UsableOriginal("[ti:Hello]\n[00:01.00]Hello, it's me\n"));
        }

        [Fact]
        public void UntimedOriginalFallsBackToTheEnhancedLyric()
        {
            // An official lyric without timing cannot scroll with the song.
            Assert.Null(LyricsController.UsableOriginal("[ti:Hello]\n[#source:genius]\nHello, it's me\n"));
            Assert.Null(LyricsController.UsableOriginal(null));
        }
    }
}
