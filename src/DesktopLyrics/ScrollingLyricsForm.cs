// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): shared renderer of the karaoke window and the lyrics panel.
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
    /// <summary>A window with the whole song scrolling through it: lines already sung above (dim), the
    /// current line in the middle (filled word by word when the lyrics have word timing), upcoming
    /// lines below. Romaji, when on, sits under each Japanese line and fills along with it.
    /// Subclasses give the window its frame, colours and size of text.</summary>
    public abstract class ScrollingLyricsForm : Form
    {
        protected virtual Color Background => Color.FromArgb(16, 16, 20);
        protected virtual Color Past => Color.FromArgb(110, 150, 150, 160);
        protected virtual Color Upcoming => Color.FromArgb(200, 225, 225, 235);
        protected virtual Color Current => Color.FromArgb(255, 245, 245, 250);
        /// <summary>Height of the band at the top and bottom where lines fade into the background (0: none).</summary>
        protected virtual float FadeFraction => 0;
        protected const float SideMargin = 32;

        protected SettingsObj Settings;
        protected LyricParser.Lyrics Lyrics;
        private readonly PlaybackClock _clock = new PlaybackClock();
        protected readonly Timer FrameTimer = new Timer { Interval = 15 };
        private readonly Stopwatch _paintWatch = Stopwatch.StartNew();
        private double _lastPaintMs;
        private float _scroll;
        private bool _scrollReady;

        private List<Line> _layout;
        private RectangleF _layoutArea;

        private sealed class Line
        {
            public LyricParser.LyricEntry Entry;
            public string Text, Romaji;
            public float MainPx, SubPx;
            public float[] Edges, Edges2;
            public float Start, Start2;
            public float Top, Height, MainHeight;
        }

        protected ScrollingLyricsForm(SettingsObj settings)
        {
            Settings = settings;
            BackColor = Background;
            ShowIcon = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FrameTimer.Tick += (s, e) => Invalidate();
            Resize += (s, e) => InvalidateLayout();
        }

        /// <summary>Where the lyrics are drawn, in client coordinates.</summary>
        protected virtual RectangleF LyricsArea => ClientRectangle;

        /// <summary>Size of a line's text in pixels before long lines are shrunk to fit.</summary>
        protected virtual float BasePx(RectangleF area) => Math.Max(18f, Math.Min(96f, area.Height / 13f));

        /// <summary>Drawn over the lyrics (frame, header, buttons).</summary>
        protected virtual void PaintChrome(Graphics g) { }

        protected bool PostToUiThread(Action action)
        {
            if (!InvokeRequired) return false;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
            return true;
        }

        protected void InvalidateLayout()
        {
            _layout = null;
            Invalidate();
        }

        public void SetLyrics(LyricParser.Lyrics lyrics)
        {
            if (PostToUiThread(() => SetLyrics(lyrics))) return;
            if (ReferenceEquals(lyrics, Lyrics)) return;
            Lyrics = lyrics;
            _scrollReady = false;
            InvalidateLayout();
        }

        public void SyncPlayback(double positionMs, bool playing)
        {
            if (PostToUiThread(() => SyncPlayback(positionMs, playing))) return;
            _clock.Sync(positionMs + (Lyrics?.Offset ?? 0), playing);
            // Keep painting while playing; when paused, paint until the scroll has settled.
            if (playing || !FrameTimer.Enabled) FrameTimer.Start();
            Invalidate();
        }

        public virtual void UpdateFromSettings(SettingsObj settings)
        {
            if (PostToUiThread(() => UpdateFromSettings(settings))) return;
            Settings = settings;
            InvalidateLayout();
        }

        // ---- layout ----

        protected FontFamily Family => Settings.Font?.FontFamily ?? SystemFonts.DefaultFont.FontFamily;
        protected FontStyle Style => Settings.Font?.Style ?? FontStyle.Regular;

        private static readonly StringFormat Centered = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.None
        };

        protected static readonly StringFormat Middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        private void EnsureLayout(Graphics g, RectangleF area)
        {
            if (_layout != null && _layoutArea == area) return;
            _layoutArea = area;
            _layout = new List<Line>();
            if (Lyrics == null) return;

            var basePx = BasePx(area);
            var width = Math.Max(50, area.Width - SideMargin * 2);
            var showLine2 = Lyrics.HasTranslation;
            var top = 0f;
            foreach (var entry in Lyrics.Entries)
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
                    new RectangleF(area.Left + SideMargin, 0, width, line.MainHeight), Centered, line.Edges);
                if (line.Romaji != null)
                {
                    line.Edges2 = new float[line.Romaji.Length];
                    line.Start2 = LyricsRenderer.MeasureCharEdges(g, line.Romaji, Family, Style, line.SubPx,
                        new RectangleF(area.Left + SideMargin, 0, width, subHeight), Centered, line.Edges2);
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
            var area = LyricsArea;
            EnsureLayout(g, area);

            var now = _paintWatch.Elapsed.TotalMilliseconds;
            var elapsed = Math.Min(200, now - _lastPaintMs);
            _lastPaintMs = now;

            if (_layout == null || _layout.Count == 0)
            {
                using (var f = new Font(Family, Math.Max(14f, BasePx(area) * 0.6f), Style, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Past))
                    g.DrawString("No lyrics", f, b, area, Middle);
                PaintChrome(g);
                if (!_clock.Playing) FrameTimer.Stop();
                return;
            }

            var t = _clock.NowMs();
            var current = KaraokeLayout.CurrentIndex(Lyrics.Entries, t);
            var tops = new float[_layout.Count];
            var heights = new float[_layout.Count];
            for (var i = 0; i < _layout.Count; i++) { tops[i] = _layout[i].Top; heights[i] = _layout[i].Height; }
            var target = KaraokeLayout.ScrollTarget(tops, heights, current, area.Height);
            _scroll = _scrollReady ? KaraokeLayout.Ease(_scroll, target, elapsed) : target;
            _scrollReady = true;

            var width = Math.Max(50, area.Width - SideMargin * 2);
            var clip = g.Save();
            g.SetClip(area);
            for (var i = 0; i < _layout.Count; i++)
            {
                var line = _layout[i];
                var y = area.Top + line.Top - _scroll;
                if (y + line.Height < area.Top || y > area.Bottom) continue;
                var baseColor = i < current ? Past : i == current ? Current : Upcoming;
                DrawText(g, line.Text, line.MainPx, new RectangleF(area.Left + SideMargin, y, width, line.MainHeight), baseColor,
                         i == current ? LitX(line.Entry.Segments, line.Edges, line.Start, line.Text.Length, t) : 0);
                if (line.Romaji != null)
                {
                    var subY = y + line.MainHeight;
                    DrawText(g, line.Romaji, line.SubPx, new RectangleF(area.Left + SideMargin, subY, width, line.SubPx * 1.35f), baseColor,
                             i == current ? LitX(line.Entry.Segments2, line.Edges2, line.Start2, line.Romaji.Length, t) : 0);
                }
            }
            PaintFade(g, area);
            g.Restore(clip);
            PaintChrome(g);

            // Paused and settled: stop the frame clock until the next sync.
            if (!_clock.Playing && Math.Abs(_scroll - target) < 0.5f) FrameTimer.Stop();
        }

        private void PaintFade(Graphics g, RectangleF area)
        {
            var band = area.Height * FadeFraction;
            if (band < 1) return;
            var clear = Color.FromArgb(0, Background);
            using (var top = new LinearGradientBrush(new RectangleF(area.Left, area.Top - 1, area.Width, band + 2), Background, clear, LinearGradientMode.Vertical))
                g.FillRectangle(top, area.Left, area.Top, area.Width, band);
            using (var bottom = new LinearGradientBrush(new RectangleF(area.Left, area.Bottom - band - 1, area.Width, band + 2), clear, Background, LinearGradientMode.Vertical))
                g.FillRectangle(bottom, area.Left, area.Bottom - band, area.Width, band);
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
                g.IntersectClip(new RectangleF(0, rect.Top, Math.Min(litX, ClientSize.Width), rect.Height));
                using (var hb = new SolidBrush(Settings.HighlightColor)) g.DrawString(text, f, hb, rect, Centered);
                g.Restore(state);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) FrameTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
