using System.Drawing;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class PanelChromeTests
    {
        private static readonly Size Client = new Size(400, 300);

        [Theory]
        [InlineData(2, 2, PanelChrome.HtTopLeft)]
        [InlineData(397, 297, PanelChrome.HtBottomRight)]
        [InlineData(2, 150, PanelChrome.HtLeft)]
        [InlineData(200, 298, PanelChrome.HtBottom)]
        [InlineData(200, 150, PanelChrome.HtCaption)]   // anywhere else drags the panel
        public void EdgesResizeAndTheRestDrags(int x, int y, int expected)
        {
            Assert.Equal(expected, PanelChrome.HitTest(Client, new Point(x, y), onButton: false));
        }

        [Fact]
        public void ButtonsAreClickableOnlyWhileTheHeaderIsShown()
        {
            var close = PanelChrome.ButtonRect(Client, PanelChrome.Button.Close);
            var p = new Point(close.X + 5, close.Y + 5);
            Assert.Equal(PanelChrome.Button.Close, PanelChrome.ButtonAt(Client, p, headerShown: true));
            Assert.Null(PanelChrome.ButtonAt(Client, p, headerShown: false));
            Assert.True(close.Right <= Client.Width - PanelChrome.Grip);   // not under the resize grip
        }

        [Fact]
        public void ButtonsDoNotOverlap()
        {
            var a = PanelChrome.ButtonRect(Client, PanelChrome.Button.FontSmaller);
            var b = PanelChrome.ButtonRect(Client, PanelChrome.Button.FontBigger);
            Assert.False(a.IntersectsWith(b));
        }

        [Fact]
        public void OpacityCyclesThroughItsSteps()
        {
            Assert.Equal(90, PanelChrome.NextOpacity(100));
            Assert.Equal(100, PanelChrome.NextOpacity(60));
            Assert.Equal(100, PanelChrome.NextOpacity(42));   // an odd saved value restarts the cycle
        }

        [Fact]
        public void TextScaleStaysInRange()
        {
            Assert.Equal(2.0f, PanelChrome.ClampScale(5f));
            Assert.Equal(0.6f, PanelChrome.ClampScale(0.1f));
        }
    }
}
