// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): continuous mode.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MusicBeePlugin
{
    public partial class FrmLyrics
    {
        // ---- continuous mode ("paper") ----
        //
        // Two lyric lines on screen: the one being sung on top, the next one dimmed below it, each with
        // its romaji/translation. When the line changes the whole sheet rolls up like a strip of paper:
        // the sung line rises and fades out, the next one moves into the top slot and lights up, and the
        // line after it fades in underneath. Every line on the sheet is its own cached bitmap, so a frame
        // is only a few blits at interpolated positions and alphas.
        private const float NextLineAlpha = 0.55f;

        private sealed class PaperLine : IDisposable
        {
            public string Line1, Line2;
            public Bitmap Bitmap;
            public RectangleF? Box;
            public RenderedBlock Word;          // highlight layer while this is the line being sung
            public float Y0, Y1, A0, A1;        // position and alpha at the start and end of the roll

            public int Height => Bitmap?.Height ?? 0;
            public bool Same(string l1, string l2) => l1 == Line1 && l2 == Line2;

            public void Dispose()
            {
                Bitmap?.Dispose();
                Bitmap = null;
                Word?.Dispose();
                Word = null;
            }
        }

        private readonly List<PaperLine> _paper = new List<PaperLine>();
        private PaperLine _paperTop;            // the line being sung; null during an instrumental gap
        private string _next1, _next2;          // the line under it
        private int _paperViewH;                // window height of the last frame pushed
        private int _paperH0, _paperH1;         // window height at the start and end of the roll

        private bool Continuous => _settings.ContinuousMode;

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static float Lerp(float a, float b, double p) => (float)(a + (b - a) * p);

        private static double Ease(double t) => 1.0 - Math.Pow(1.0 - Math.Min(1.0, t), 3.0);

        private double PaperProgress() =>
            !_animating || _animDuration <= 0 ? 1.0 : Math.Min(1.0, _animWatch.Elapsed.TotalMilliseconds / _animDuration);

        private PaperLine RenderPaperLine(string l1, string l2, bool withHighlight)
        {
            using (var g = CreateGraphics())
            {
                var block = l2 == null
                    ? LyricsRenderer.Render1LineBlock(l1, g, withHighlight, bakeBox: false)
                    : LyricsRenderer.Render2LineBlock(l1, l2, g, withHighlight, bakeBox: false);
                var line = new PaperLine { Line1 = l1, Line2 = l2, Box = block.BoxRect, Bitmap = block.DetachBase() };
                if (withHighlight) line.Word = block;
                else block.Dispose();
                return line;
            }
        }

        /// <summary>Bakes the lit part of a line into its bitmap as it leaves the top slot, so it rolls
        /// away looking the way it was sung instead of losing its colour.</summary>
        private void FlattenHighlight(PaperLine line)
        {
            if (line.Word == null) return;
            if (_settings.WordHighlight && line.Bitmap != null)
                using (var g = Graphics.FromImage(line.Bitmap))
                    DrawHighlight(g, line.Word, 0, line.Bitmap.Width, line.Bitmap.Height, 1f);
            line.Word.Dispose();
            line.Word = null;
        }

        private void DisposePaper()
        {
            foreach (var line in _paper) line.Dispose();
            _paper.Clear();
            _paperTop = null;
            _paperViewH = 0;
        }

        /// <summary>Full rebuild at rest (settings changed, mode switched). Never animates.</summary>
        private void PaperRedraw()
        {
            StopAnimation();
            _blockCurrent?.Dispose();   // classic-mode state is not used while continuous
            _blockCurrent = null;
            ReplaceWordBlock(null);
            DisposePaper();
            PaperTransition(animate: false);
        }

        /// <summary>Rolls the sheet to the current (_line1/_line2) and next (_next1/_next2) lines.</summary>
        private void PaperTransition(bool animate)
        {
            // Start from what is on screen right now, so a roll interrupted by the next one carries on.
            var p = Ease(PaperProgress());
            foreach (var line in _paper)
            {
                line.Y0 = Lerp(line.Y0, line.Y1, p);
                line.A0 = Lerp(line.A0, line.A1, p);
            }
            StopAnimationClock();
            _animating = false;

            var t1 = NullIfEmpty(_line1);
            var t2 = t1 == null ? null : NullIfEmpty(_line2);
            var n1 = NullIfEmpty(_next1);
            var n2 = n1 == null ? null : NullIfEmpty(_next2);
            var oldTop = _paperTop;

            // The line that carries on: normally the old "next" moving up into the top slot (or, entering
            // an instrumental gap, staying on as the upcoming line). Old "next" is searched before the old
            // top so a line repeated twice in a row still rolls.
            var want1 = t1 ?? n1;
            var want2 = t1 != null ? t2 : n2;
            PaperLine keep = null;
            if (want1 != null)
            {
                foreach (var line in _paper)
                    if (line != oldTop && line.A1 > 0 && line.Same(want1, want2)) { keep = line; break; }
                if (keep == null && oldTop != null && oldTop.Same(want1, want2)) keep = oldTop;
            }
            var keepIsTop = keep != null && t1 != null;

            // How far the sheet rolls: the kept line ends at the top; with nothing kept, everything on
            // screen rolls out and the new lines come up from underneath.
            var shift = keep != null ? keep.Y0 : _paperViewH;

            foreach (var line in _paper)
            {
                if (line == keep) continue;
                line.Y1 = line.Y0 - shift;
                line.A1 = 0;
            }
            if (oldTop != null && oldTop != keep) FlattenHighlight(oldTop);

            PaperLine top = null, next = null;
            if (t1 != null)
            {
                top = RenderPaperLine(t1, t2, _settings.WordHighlight && _segs1 != null);
                top.Y0 = keepIsTop ? keep.Y0 : shift;
                top.A0 = keepIsTop ? keep.A0 : 0;
                top.Y1 = 0;
                top.A1 = 1;
            }
            var nextY = top?.Height ?? 0;
            if (n1 != null)
            {
                next = RenderPaperLine(n1, n2, false);
                var keepIsNext = keep != null && !keepIsTop;
                next.Y0 = keepIsNext ? keep.Y0 : nextY + shift;
                next.A0 = keepIsNext ? keep.A0 : 0;
                next.Y1 = nextY;
                next.A1 = NextLineAlpha;
            }
            // The kept line is replaced by a fresh render for its new role (lit layer on, or off).
            if (keep != null)
            {
                _paper.Remove(keep);
                keep.Dispose();
            }
            if (top != null) _paper.Add(top);
            if (next != null) _paper.Add(next);
            _paperTop = top;

            _paperH0 = _paperViewH;
            _paperH1 = 0;
            foreach (var line in _paper)
                if (line.A1 > 0) _paperH1 = Math.Max(_paperH1, (int)Math.Ceiling(line.Y1) + line.Height);

            if (!animate || !_settings.SmoothScroll || _paperH0 == 0 && _paperH1 == 0)
            {
                FinishPaper();
                return;
            }

            _animDuration = Math.Max(AnimMinDuration, Math.Min(AnimMaxDuration, _settings.SmoothScrollDuration));
            _animWatch.Restart();
            _hasContent = true;   // keep the window up while the last line rolls away
            ApplyVisibility();
            _animating = true;
            StartAnimationClock();
            PaperTick();
        }

        private void PaperTick()
        {
            var t = PaperProgress();
            if (t >= 1.0)
            {
                FinishPaper();
                return;
            }
            PresentPaper(Ease(t), Math.Max(_paperH0, _paperH1));
        }

        private void FinishPaper()
        {
            StopAnimationClock();
            _animating = false;
            for (var i = _paper.Count - 1; i >= 0; i--)
            {
                var line = _paper[i];
                if (line.A1 <= 0)
                {
                    line.Dispose();
                    _paper.RemoveAt(i);
                    continue;
                }
                line.Y0 = line.Y1;
                line.A0 = line.A1;
            }
            _hasContent = _paper.Count > 0;
            if (_hasContent) PresentPaper(1.0, _paperH1);
            else _paperViewH = 0;
            ApplyVisibility();
            StartHighlight();
        }

        private void PresentPaperRest() => PresentPaper(1.0, _paperH1);

        private void PresentPaper(double p, int viewHeight)
        {
            var width = 0;
            foreach (var line in _paper) width = Math.Max(width, line.Bitmap?.Width ?? 0);
            if (width <= 0 || viewHeight <= 0) return;

            using (var frame = new Bitmap(width, viewHeight))
            {
                using (var g = Graphics.FromImage(frame))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    var box = PaperBox(p, width, viewHeight);
                    if (box.HasValue) LyricsRenderer.FillBox(g, box.Value);
                    foreach (var line in _paper)
                    {
                        if (line.Bitmap == null) continue;
                        var y = (int)Math.Round(Lerp(line.Y0, line.Y1, p));
                        var a = Lerp(line.A0, line.A1, p);
                        if (a <= 0.004f || y >= viewHeight || y + line.Height <= 0) continue;
                        DrawFaded(g, line.Bitmap, y, a);
                        if (line == _paperTop && line.Word != null && _settings.WordHighlight)
                            DrawHighlight(g, line.Word, y, line.Bitmap.Width, line.Bitmap.Height, a);
                    }
                }

                if (Width != width) Width = width;
                if (Height != viewHeight) Height = viewHeight;
                _paperViewH = viewHeight;
                GdiplusHelper.SetBitmap(frame, TextAlpha, Handle, Left, Top, Width, Height);
            }
        }

        /// <summary>One box behind the sheet, moving smoothly from the lines it covered before the roll
        /// to the lines it covers after it.</summary>
        private RectangleF? PaperBox(double p, int width, int viewHeight)
        {
            RectangleF? from = null, to = null;
            foreach (var line in _paper)
            {
                if (line.A0 > 0) from = Union(from, Shifted(line.Box, (int)Math.Round(line.Y0)));
                if (line.A1 > 0) to = Union(to, Shifted(line.Box, (int)Math.Round(line.Y1)));
            }
            RectangleF? box;
            if (from.HasValue && to.HasValue)
            {
                var a = from.Value;
                var b = to.Value;
                box = RectangleF.FromLTRB(Lerp(a.Left, b.Left, p), Lerp(a.Top, b.Top, p),
                                          Lerp(a.Right, b.Right, p), Lerp(a.Bottom, b.Bottom, p));
            }
            else
            {
                // Rolling out with nothing after it (or in from nothing): the box travels with the lines.
                box = null;
                foreach (var line in _paper)
                    box = Union(box, Shifted(line.Box, (int)Math.Round(Lerp(line.Y0, line.Y1, p))));
            }
            if (!box.HasValue) return null;
            var r = box.Value;
            if (FixedBox)
            {
                // Fixed size per song: the widest line of the song sets the box width.
                r = RectangleF.FromLTRB(_songBox.Value.Left, r.Top, _songBox.Value.Right, r.Bottom);
            }
            r = RectangleF.Intersect(r, new RectangleF(0, 0, width, viewHeight));
            return r.Width > 0 && r.Height > 0 ? r : (RectangleF?)null;
        }

        private static void DrawFaded(Graphics g, Image image, int y, float alpha)
        {
            if (alpha >= 0.996f)
            {
                g.DrawImage(image, 0, y, image.Width, image.Height);
                return;
            }
            using (var attributes = AlphaAttributes(alpha))
                g.DrawImage(image, new Rectangle(0, y, image.Width, image.Height),
                            0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        private static ImageAttributes AlphaAttributes(float alpha)
        {
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
            return attributes;
        }
    }
}
