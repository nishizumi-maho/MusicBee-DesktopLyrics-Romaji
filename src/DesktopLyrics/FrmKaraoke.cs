// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): karaoke window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace MusicBeePlugin
{
    /// <summary>A normal, resizable window with the whole song: lines already sung above (dim), the
    /// current line in the middle (filled word by word when the lyrics have word timing), upcoming
    /// lines below. Romaji, when on, sits under each Japanese line and fills along with it.
    /// F11 or a double-click toggles fullscreen; Esc leaves it.</summary>
    public sealed class FrmKaraoke : Form
    {
        private static readonly Color Background = Color.FromArgb(16, 16, 20);
        private static readonly Color Past = Color.FromArgb(110, 150, 150, 160);
        private static readonly Color Upcoming = Color.FromArgb(200, 225, 225, 235);
        private static readonly Color Current = Color.FromArgb(255, 245, 245, 250);
        private const float SideMargin = 32;

        private SettingsObj _settings;
        private LyricParser.Lyrics _lyrics;
        private readonly PlaybackClock _clock = new PlaybackClock();
        private readonly Timer _frameTimer = new Timer { Interval = 15 };
        private readonly Stopwatch _paintWatch = Stopwatch.StartNew();
        private double _lastPaintMs;
        private float _scroll;
        private bool _scrollReady;

        private List<Line> _layout;
        private Size _layoutSize;

        private bool _fullscreen;
        private Rectangle _restoreBounds;

        private sealed class Line
        {
            public LyricParser.LyricEntry Entry;
            public string Text, Romaji;
            public float MainPx, SubPx;
            public float[] Edges, Edges2;
            public float Start, Start2;
            public float Top, Height, MainHeight;
        }

        public FrmKaraoke(SettingsObj settings)
        {
            _settings = settings;
            Text = "Desktop Lyrics — Karaoke";
            BackColor = Background;
            ForeColor = Current;
            KeyPreview = true;
            MinimumSize = new Size(360, 240);
            ShowIcon = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            StartPosition = FormStartPosition.Manual;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var w = settings.KaraokeW > 0 ? settings.KaraokeW : Math.Min(1000, wa.Width * 2 / 3);
            var h = settings.KaraokeH > 0 ? settings.KaraokeH : Math.Min(700, wa.Height * 2 / 3);
            var bounds = new Rectangle(settings.KaraokeX >= 0 ? settings.KaraokeX : wa.Left + (wa.Width - w) / 2,
                                       settings.KaraokeY >= 0 ? settings.KaraokeY : wa.Top + (wa.Height - h) / 2, w, h);
            // A saved position on a monitor that is no longer there would open the window off-screen.
            Bounds = Screen.AllScreens.Length > 0 && Array.Exists(Screen.AllScreens, s => s.WorkingArea.IntersectsWith(bounds))
                ? bounds
                : new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);

            _frameTimer.Tick += (s, e) => Invalidate();
            Shown += (s, e) => { if (_settings.KaraokeFullscreen) SetFullscreen(true); };
            Resize += (s, e) => { _layout = null; };
            DoubleClick += (s, e) => SetFullscreen(!_fullscreen);
            KeyDown += OnKeyDown;
            FormClosing += (s, e) =>
            {
                _frameTimer.Stop();
                var b = _fullscreen ? _restoreBounds : (WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
                _settings.KaraokeX = b.X;
                _settings.KaraokeY = b.Y;
                _settings.KaraokeW = b.Width;
                _settings.KaraokeH = b.Height;
                _settings.KaraokeFullscreen = _fullscreen;
            };
        }

        private bool PostToUiThread(Action action)
        {
            if (!InvokeRequired) return false;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
            return true;
        }

        public void SetLyrics(LyricParser.Lyrics lyrics)
        {
            if (PostToUiThread(() => SetLyrics(lyrics))) return;
            if (ReferenceEquals(lyrics, _lyrics)) return;
            _lyrics = lyrics;
            _layout = null;
            _scrollReady = false;
            Invalidate();
        }

        public void SyncPlayback(double positionMs, bool playing)
        {
            if (PostToUiThread(() => SyncPlayback(positionMs, playing))) return;
            _clock.Sync(positionMs + (_lyrics?.Offset ?? 0), playing);
            // Keep painting while playing; when paused, paint until the scroll has settled.
            if (playing || !_frameTimer.Enabled) _frameTimer.Start();
            Invalidate();
        }

        public void UpdateFromSettings(SettingsObj settings)
        {
            if (PostToUiThread(() => UpdateFromSettings(settings))) return;
            _settings = settings;
            _layout = null;
            Invalidate();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11) SetFullscreen(!_fullscreen);
            else if (e.KeyCode == Keys.Escape && _fullscreen) SetFullscreen(false);
            else return;
            e.Handled = true;
        }

        private void SetFullscreen(bool on)
        {
            if (on == _fullscreen) return;
            if (on)
            {
                _restoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = _restoreBounds;
            }
            _fullscreen = on;
            _layout = null;
            Invalidate();
        }

        // ---- layout ----

        private FontFamily Family => _settings.Font?.FontFamily ?? SystemFonts.DefaultFont.FontFamily;
        private FontStyle Style => _settings.Font?.Style ?? FontStyle.Regular;

        private static readonly StringFormat Centered = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.None
        };

        private static readonly StringFormat Middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        private void EnsureLayout(Graphics g)
        {
            if (_layout != null && _layoutSize == ClientSize) return;
            _layoutSize = ClientSize;
            _layout = new List<Line>();
            if (_lyrics == null) return;

            var basePx = Math.Max(18f, Math.Min(96f, ClientSize.Height / 13f));
            var width = Math.Max(50, ClientSize.Width - SideMargin * 2);
            var showLine2 = _lyrics.HasTranslation;
            var top = 0f;
            foreach (var entry in _lyrics.Entries)
            {
                var line = new Line
                {
                    Entry = entry,
                    Text = entry.LyricLine1 ?? "",
                    Romaji = showLine2 && !string.IsNullOrEmpty(entry.LyricLine2) ? entry.LyricLine2 : null
                };
                line.MainPx = FitPx(g, line.Text, basePx, width);
                line.SubPx = line.Romaji == null ? 0 : FitPx(g, line.Romaji, basePx * 0.55f, width);
                line.MainHeight = line.MainPx * 1.35f;
                var subHeight = line.Romaji == null ? 0 : line.SubPx * 1.35f;
                line.Height = line.MainHeight + subHeight + basePx * 0.45f;
                line.Top = top;
                top += line.Height;

                line.Edges = new float[line.Text.Length];
                line.Start = LyricsRenderer.MeasureCharEdges(g, line.Text, Family, Style, line.MainPx,
                    new RectangleF(SideMargin, 0, width, line.MainHeight), Centered, line.Edges);
                if (line.Romaji != null)
                {
                    line.Edges2 = new float[line.Romaji.Length];
                    line.Start2 = LyricsRenderer.MeasureCharEdges(g, line.Romaji, Family, Style, line.SubPx,
                        new RectangleF(SideMargin, 0, width, subHeight), Centered, line.Edges2);
                }
                _layout.Add(line);
            }
        }

        /// <summary>Font size in pixels for a line, shrunk so a long line still fits the window width.</summary>
        private float FitPx(Graphics g, string text, float px, float width)
        {
            if (string.IsNullOrEmpty(text)) return px;
            using (var f = new Font(Family, px, Style, GraphicsUnit.Pixel))
            {
                var w = g.MeasureString(text, f, PointF.Empty, Centered).Width;
                return w <= width ? px : Math.Max(10f, px * width / w);
            }
        }

        // ---- painting ----

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Background);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            EnsureLayout(g);

            var now = _paintWatch.Elapsed.TotalMilliseconds;
            var elapsed = Math.Min(200, now - _lastPaintMs);
            _lastPaintMs = now;

            if (_layout == null || _layout.Count == 0)
            {
                using (var f = new Font(Family, 22, Style, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Past))
                    g.DrawString("No lyrics", f, b, ClientRectangle, Middle);
                if (!_clock.Playing) _frameTimer.Stop();
                return;
            }

            var t = _clock.NowMs();
            var current = KaraokeLayout.CurrentIndex(_lyrics.Entries, t);
            var tops = new float[_layout.Count];
            var heights = new float[_layout.Count];
            for (var i = 0; i < _layout.Count; i++) { tops[i] = _layout[i].Top; heights[i] = _layout[i].Height; }
            var target = KaraokeLayout.ScrollTarget(tops, heights, current, ClientSize.Height);
            _scroll = _scrollReady ? KaraokeLayout.Ease(_scroll, target, elapsed) : target;
            _scrollReady = true;

            var width = Math.Max(50, ClientSize.Width - SideMargin * 2);
            for (var i = 0; i < _layout.Count; i++)
            {
                var line = _layout[i];
                var y = line.Top - _scroll;
                if (y + line.Height < 0 || y > ClientSize.Height) continue;
                var baseColor = i < current ? Past : i == current ? Current : Upcoming;
                DrawText(g, line.Text, line.MainPx, new RectangleF(SideMargin, y, width, line.MainHeight), baseColor,
                         i == current ? LitX(line.Entry.Segments, line.Edges, line.Start, line.Text.Length, t) : 0);
                if (line.Romaji != null)
                {
                    var subY = y + line.MainHeight;
                    DrawText(g, line.Romaji, line.SubPx, new RectangleF(SideMargin, subY, width, line.SubPx * 1.35f), baseColor,
                             i == current ? LitX(line.Entry.Segments2, line.Edges2, line.Start2, line.Romaji.Length, t) : 0);
                }
            }

            // Paused and settled: stop the frame clock until the next sync.
            if (!_clock.Playing && Math.Abs(_scroll - target) < 0.5f) _frameTimer.Stop();
        }

        /// <summary>x the fill has reached on a line. Without word timing the current line is lit whole.</summary>
        private static float LitX(List<LyricParser.WordSegment> segs, float[] edges, float start, int length, double t)
        {
            if (edges == null || edges.Length == 0) return 0;
            if (segs == null) return float.MaxValue;
            var lit = WordTimeline.LitChars(segs, t);
            if (lit <= 0) return 0;
            if (lit >= length) return edges[edges.Length - 1];
            var i = (int)Math.Floor(lit);
            var left = i == 0 ? start : edges[i - 1];
            return left + (float)((lit - i) * (edges[i] - left));
        }

        private void DrawText(Graphics g, string text, float px, RectangleF rect, Color color, float litX)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var f = new Font(Family, px, Style, GraphicsUnit.Pixel))
            {
                using (var b = new SolidBrush(color)) g.DrawString(text, f, b, rect, Centered);
                if (litX <= 0) return;
                var state = g.Save();
                g.SetClip(new RectangleF(0, rect.Top, Math.Min(litX, ClientSize.Width), rect.Height));
                using (var hb = new SolidBrush(_settings.HighlightColor)) g.DrawString(text, f, hb, rect, Centered);
                g.Restore(state);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _frameTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
