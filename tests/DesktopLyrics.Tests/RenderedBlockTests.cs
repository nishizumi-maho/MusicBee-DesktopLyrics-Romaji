using System.Drawing;
using System.Linq;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class RenderedBlockTests
    {
        private static RenderedBlock Render(bool box, string line1, string line2)
        {
            var s = SettingsObj.GenerateDefault();
            s.BoxBackground = box;
            LyricsRenderer.SetDpi(96);
            LyricsRenderer.UpdateFromSettings(s, 1200);
            using (var bmp = new Bitmap(10, 10))
            using (var g = Graphics.FromImage(bmp))
                return line2 == null ? LyricsRenderer.Render1LineBlock(line1, g, true)
                                     : LyricsRenderer.Render2LineBlock(line1, line2, g, true);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EdgesIncreaseAndStayInsideTheBitmap(bool box)
        {
            using (var b = Render(box, "言葉が人を", "kotoba ga hito wo"))
            {
                Assert.Equal(2, b.CharEdges.Length);
                Assert.Equal(5, b.CharEdges[0].Length);
                Assert.Equal(17, b.CharEdges[1].Length);
                foreach (var edges in b.CharEdges)
                {
                    Assert.True(edges.Zip(edges.Skip(1), (a, c) => c >= a).All(x => x));
                    Assert.True(edges.Last() > edges.First());
                    Assert.True(edges.Last() <= b.Base.Width);
                }
                Assert.Equal(b.Base.Size, b.Highlight.Size);
            }
        }

        [Fact]
        public void LitXStartsAtTheFirstGlyphAndEndsAtTheLast()
        {
            using (var b = Render(false, "eu vou", null))
            {
                var start = b.LineStartX[0];
                Assert.True(start > 0 && start < b.CharEdges[0][0]);
                Assert.Equal(start, b.LitX(0, 0.0001), 1);
                Assert.Equal(b.CharEdges[0].Last(), b.LitX(0, 6));
                Assert.Equal((start + b.CharEdges[0][0]) / 2, b.LitX(0, 0.5), 1);
            }
        }

        [Fact]
        public void HighlightLayerHasNoBackground()
        {
            using (var b = Render(true, "eu vou", null))
            {
                var r = b.LineRects[0];
                var y = (int)(r.Top + r.Height / 2);
                Assert.Equal(0, b.Highlight.GetPixel(2, y).A);              // left margin: transparent
                Assert.Contains(Enumerable.Range(0, b.Highlight.Width), x => b.Highlight.GetPixel(x, y).A > 0); // the text is there
            }
        }

        [Fact]
        public void OldEntryPointsStillReturnTheBaseBitmap()
        {
            var s = SettingsObj.GenerateDefault();
            LyricsRenderer.UpdateFromSettings(s, 800);
            using (var bmp = new Bitmap(10, 10))
            using (var g = Graphics.FromImage(bmp))
            using (var plain = LyricsRenderer.Render1LineLyrics("abc", g))
                Assert.Equal(800, plain.Width);
        }
    }
}
