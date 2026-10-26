// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): lyrics panel (BETA), in the style of MiniLyrics.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace MusicBeePlugin
{
    /// <summary>Geometry and small rules of the lyrics panel, kept apart from the window so they can be tested.</summary>
    public static class PanelChrome
    {
        public const int HtClient = 1, HtCaption = 2, HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13,
                         HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
        public const int Grip = 8;           // px from an edge that resize the panel
        public const int HeaderHeight = 34;
        public const int ButtonSize = 26;

        public enum Button { FontSmaller, FontBigger, Opacity, Close }
        public static readonly Button[] Buttons = { Button.FontSmaller, Button.FontBigger, Button.Opacity, Button.Close };

        /// <summary>Header buttons, right-aligned in the header.</summary>
        public static Rectangle ButtonRect(Size client, Button b)
        {
            var slot = Buttons.Length - Array.IndexOf(Buttons, b);   // 1 = rightmost
            var x = client.Width - 10 - slot * (ButtonSize + 4) + 4;   // clear of the resize grip
            return new Rectangle(x, (HeaderHeight - ButtonSize) / 2, ButtonSize, ButtonSize);
        }

        public static Button? ButtonAt(Size client, Point p, bool headerShown)
        {
            if (!headerShown) return null;
            foreach (var b in Buttons)
                if (ButtonRect(client, b).Contains(p)) return b;
            return null;
        }

        /// <summary>WM_NCHITTEST answer: edges and corners resize, buttons are clicked, anything else drags.</summary>
        public static int HitTest(Size client, Point p, bool onButton)
        {
            if (onButton) return HtClient;
            bool l = p.X < Grip, r = p.X >= client.Width - Grip, t = p.Y < Grip, b = p.Y >= client.Height - Grip;
            if (t && l) return HtTopLeft;
            if (t && r) return HtTopRight;
            if (b && l) return HtBottomLeft;
            if (b && r) return HtBottomRight;
            if (l) return HtLeft;
            if (r) return HtRight;
            if (t) return HtTop;
            if (b) return HtBottom;
            return HtCaption;
        }

        private static readonly int[] OpacitySteps = { 100, 90, 75, 60 };

        public static int NextOpacity(int percent)
        {
            var i = Array.IndexOf(OpacitySteps, percent);
            return OpacitySteps[(i + 1) % OpacitySteps.Length];
        }

        public static float ClampScale(float scale) => Math.Max(0.6f, Math.Min(2.0f, scale));
    }

    /// <summary>A floating panel with the song scrolling through it: borderless with rounded corners and a
    /// shadow, always on top, slightly translucent, lines fading at the top and bottom. Hovering shows a
    /// header with the song, a BETA badge and buttons (text smaller/bigger, opacity, close). Drag anywhere
    /// to move it; drag an edge to resize it.</summary>
    public sealed class FrmLyricsPanel : ScrollingLyricsForm
    {
        protected override Color Background => Color.FromArgb(22, 22, 28);
        protected override Color Past => Color.FromArgb(105, 105, 116);
        protected override Color Upcoming => Color.FromArgb(160, 160, 172);
        protected override float FadeFraction => 0.2f;

        private static readonly Color HeaderBack = Color.FromArgb(34, 34, 42);
        private static readonly Color HeaderText = Color.FromArgb(220, 220, 228);
        private static readonly Color ButtonHover = Color.FromArgb(58, 58, 70);

        private readonly Timer _hoverTimer = new Timer { Interval = 120 };
        private bool _hover;
        private PanelChrome.Button? _hotButton;
        private string _caption = "";

        public FrmLyricsPanel(SettingsObj settings) : base(settings)
        {
            Text = "Lyrics Panel (BETA)";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            MinimumSize = new Size(260, 160);
            Opacity = Math.Max(30, Math.Min(100, settings.PanelOpacity)) / 100.0;

            StartPosition = FormStartPosition.Manual;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var w = settings.PanelW > 0 ? settings.PanelW : 560;
            var h = settings.PanelH > 0 ? settings.PanelH : 360;
            var bounds = new Rectangle(settings.PanelX >= 0 ? settings.PanelX : wa.Right - w - 24,
                                       settings.PanelY >= 0 ? settings.PanelY : wa.Bottom - h - 24, w, h);
            // A saved position on a monitor that is no longer there would open the panel off-screen.
            Bounds = Array.Exists(Screen.AllScreens, s => s.WorkingArea.IntersectsWith(bounds))
                ? bounds
                : new Rectangle(wa.Right - w - 24, wa.Bottom - h - 24, w, h);

            _hoverTimer.Tick += (s, e) =>
            {
                var hover = Bounds.Contains(Cursor.Position);
                if (hover == _hover) return;
                _hover = hover;
                if (!hover) _hotButton = null;
                Invalidate();
            };
            _hoverTimer.Start();
            MouseMove += (s, e) =>
            {
                var hot = PanelChrome.ButtonAt(ClientSize, e.Location, _hover);
                if (hot == _hotButton) return;
                _hotButton = hot;
                Invalidate();
            };
            MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && PanelChrome.ButtonAt(ClientSize, e.Location, _hover) is PanelChrome.Button b)
                    Press(b);
            };
            FormClosing += (s, e) =>
            {
                FrameTimer.Stop();
                _hoverTimer.Stop();
                Settings.PanelX = Bounds.X;
                Settings.PanelY = Bounds.Y;
                Settings.PanelW = Bounds.Width;
                Settings.PanelH = Bounds.Height;
            };
        }

        /// <summary>"Title — Artist", shown in the header.</summary>
        public void SetCaption(string caption)
        {
            if (PostToUiThread(() => SetCaption(caption))) return;
            _caption = caption ?? "";
            if (_hover) Invalidate();
        }

        private void Press(PanelChrome.Button b)
        {
            switch (b)
            {
                case PanelChrome.Button.FontSmaller:
                case PanelChrome.Button.FontBigger:
                    Settings.PanelFontScale = PanelChrome.ClampScale(Settings.PanelFontScale + (b == PanelChrome.Button.FontBigger ? 0.1f : -0.1f));
                    InvalidateLayout();
                    break;
                case PanelChrome.Button.Opacity:
                    Settings.PanelOpacity = PanelChrome.NextOpacity(Settings.PanelOpacity);
                    Opacity = Settings.PanelOpacity / 100.0;
                    break;
                case PanelChrome.Button.Close:
                    Close();
                    break;
            }
        }

        protected override RectangleF LyricsArea => new RectangleF(0, 10, ClientSize.Width, Math.Max(1, ClientSize.Height - 20));

        protected override float BasePx(RectangleF area) =>
            Math.Max(13f, Math.Min(72f, area.Height / 9f)) * PanelChrome.ClampScale(Settings.PanelFontScale);

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                return cp;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var round = 2;   // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND (Windows 11; ignored before)
            try { DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); }
            catch (Exception) { }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDBLCLK = 0xA3;
            if (m.Msg == WM_NCLBUTTONDBLCLK) return;   // no maximise on double-click
            if (m.Msg == WM_NCHITTEST)
            {
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                m.Result = (IntPtr)PanelChrome.HitTest(ClientSize, p, PanelChrome.ButtonAt(ClientSize, p, _hover) != null);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void PaintChrome(Graphics g)
        {
            using (var border = new Pen(Color.FromArgb(60, 255, 255, 255)))
                g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            if (!_hover) return;

            using (var back = new SolidBrush(HeaderBack))
                g.FillRectangle(back, 0, 0, ClientSize.Width, PanelChrome.HeaderHeight);

            // BETA badge
            var x = 10f;
            using (var badgeFont = new Font("Segoe UI", 8f, FontStyle.Bold))
            {
                var size = g.MeasureString("BETA", badgeFont);
                var badge = new RectangleF(x, (PanelChrome.HeaderHeight - size.Height - 2) / 2, size.Width + 6, size.Height + 2);
                using (var path = Rounded(badge, 4))
                using (var fill = new SolidBrush(Settings.HighlightColor))
                    g.FillPath(fill, path);
                using (var text = new SolidBrush(Color.FromArgb(20, 20, 24)))
                    g.DrawString("BETA", badgeFont, text, badge, Middle);
                x = badge.Right + 8;
            }

            var firstButton = PanelChrome.ButtonRect(ClientSize, PanelChrome.Buttons[0]).Left;
            using (var font = new Font("Segoe UI", 9f))
            using (var text = new SolidBrush(HeaderText))
            using (var fmt = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(_caption, font, text, new RectangleF(x, 0, Math.Max(0, firstButton - x - 6), PanelChrome.HeaderHeight), fmt);

            using (var font = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var text = new SolidBrush(HeaderText))
                foreach (var b in PanelChrome.Buttons)
                {
                    var r = PanelChrome.ButtonRect(ClientSize, b);
                    if (b == _hotButton)
                        using (var path = Rounded(r, 5))
                        using (var hot = new SolidBrush(b == PanelChrome.Button.Close ? Color.FromArgb(196, 43, 28) : ButtonHover))
                            g.FillPath(hot, path);
                    var label = b == PanelChrome.Button.FontSmaller ? "A−" : b == PanelChrome.Button.FontBigger ? "A+"
                              : b == PanelChrome.Button.Opacity ? "◐" : "✕";
                    g.DrawString(label, font, text, r, Middle);
                }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hoverTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
