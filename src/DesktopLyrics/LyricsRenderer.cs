// Modified by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): Optional colour for the second line; optional single padded box background behind all lines.
// Original work: Copyright Charlie Jiang, licensed under the Apache License 2.0.
using System;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public static class LyricsRenderer
    {
        private static readonly Brush ShadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        private const int ShadowOffset = 2;
        private const int BackgroundOffsetH = 10;
        private const int BackgroundOffsetV = 6;

        private static readonly StringFormat Format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };

        private static Pen _borderPen;
        private static Font _mainFont, _subFont;
        private static Color _color1, _color2;
        private static GradientType _type;
        private static int _width = 1024;
        private static int _dpi = 72;
        private static int _backgroundOpacity = 40;
        private static bool _useLine2Color;
        private static Color _line2Color;
        private static bool _boxBackground;
        private static Color _highlightColor = Color.FromArgb(0x50, 0xE6, 0xF0);

            // Thread unsafe!!!
        public static void UpdateFromSettings(SettingsObj settings, int width)
        {
            _mainFont = settings.Font ?? SystemFonts.DefaultFont;
            _subFont = new Font(_mainFont.FontFamily, _mainFont.Size * 0.8f, _mainFont.Style, _mainFont.Unit, _mainFont.GdiCharSet);
            _borderPen?.Dispose();
            _borderPen = new Pen(settings.BorderColor, 2f)
            {
                LineJoin = LineJoin.Round,
                EndCap = LineCap.Round,
                StartCap = LineCap.Round
            };
            _type = (GradientType)settings.GradientType;
            _color1 = settings.Color1;
            _color2 = settings.Color2;
            _backgroundOpacity = settings.BackgroundOpacity;
            _useLine2Color = settings.UseLine2Color;
            _line2Color = settings.Line2Color;
            _boxBackground = settings.BoxBackground;
            _highlightColor = settings.HighlightColor;
            switch ((AlignmentType)settings.AlignmentType)
            {
                case AlignmentType.Left:
                    Format.Alignment = StringAlignment.Near;
                    break;
                case AlignmentType.Right:
                    Format.Alignment = StringAlignment.Far;
                    break;
                case AlignmentType.Center:
                default:
                    Format.Alignment = StringAlignment.Center;
                    break;
            }

            _width = width;
        }

        public static void SetDpi(int deviceDpi)
        {
            _dpi = deviceDpi;
        }

        public static Bitmap Render1LineLyrics(string line1, IDeviceContext dc)
        {
            using (var block = Render1LineBlock(line1, dc, false)) return block.DetachBase();
        }

        public static Bitmap Render2LineLyrics(string line1, string line2, IDeviceContext dc)
        {
            using (var block = Render2LineBlock(line1, line2, dc, false)) return block.DetachBase();
        }

        /// <param name="bakeBox">With the single-box background on: true draws the box into the bitmap
        /// (as before); false only reports it in <see cref="RenderedBlock.BoxRect"/>, so the window can draw
        /// one continuous box behind two blocks while they slide.</param>
        public static RenderedBlock Render1LineBlock(string line1, IDeviceContext dc, bool withHighlight, bool bakeBox = true) =>
            RenderBlock(new[] { line1 }, new[] { _mainFont }, dc, withHighlight, bakeBox);

        public static RenderedBlock Render2LineBlock(string line1, string line2, IDeviceContext dc, bool withHighlight, bool bakeBox = true) =>
            RenderBlock(new[] { line1, line2 }, new[] { _mainFont, _subFont }, dc, withHighlight, bakeBox);

        /// <summary>Draws the single-box background (rounded, background opacity) at <paramref name="box"/>.</summary>
        public static void FillBox(Graphics g, RectangleF box)
        {
            if (_backgroundOpacity <= 0 || box.Width <= 0 || box.Height <= 0) return;
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(box, 12 * (_dpi / 96f)))
            using (var brush = new SolidBrush(Color.FromArgb(_backgroundOpacity, 0, 0, 0)))
                g.FillPath(brush, path);
            g.SmoothingMode = smoothing;
        }

        /// <summary>Base block exactly as before; optionally a same-size highlight layer holding only the
        /// text in the highlight colour (no background, no shadow), plus each character's right edge so
        /// the window can reveal the layer up to the sung position.</summary>
        private static RenderedBlock RenderBlock(string[] texts, Font[] fonts, IDeviceContext dc, bool withHighlight, bool bakeBox)
        {
            RectangleF? boxRect = null;
            var n = texts.Length;
            var baseLines = new Bitmap[n];
            var hiLines = withHighlight ? new Bitmap[n] : null;
            var bounds = new RectangleF[n];
            var edges = new float[n][];
            var starts = new float[n];
            try
            {
                for (var i = 0; i < n; i++)
                {
                    baseLines[i] = RenderLyrics(texts[i], fonts[i], dc, i == 1, out bounds[i], out edges[i], out starts[i], false);
                    if (withHighlight) hiLines[i] = RenderLyrics(texts[i], fonts[i], dc, i == 1, out _, out _, out _, true);
                }
                var tops = new float[n];
                if (n > 1) tops[1] = baseLines[0].Height * 0.9f; // TODO: a better approach to reduce line space for a more compact view...

                Bitmap baseBitmap;
                float offsetY = 0;
                if (_boxBackground)
                {
                    baseBitmap = ComposeInBox(baseLines, tops, bounds, bakeBox, out boxRect);
                    offsetY = BoxPadV;
                }
                else if (n == 1)
                {
                    baseBitmap = baseLines[0];
                    baseLines[0] = null;   // handed over, not disposed below
                }
                else
                {
                    baseBitmap = new Bitmap(Math.Max(baseLines[0].Width, baseLines[1].Width), baseLines[0].Height + baseLines[1].Height);
                    using (var g = Graphics.FromImage(baseBitmap))
                    {
                        g.DrawImage(baseLines[0], new PointF(0, 0));
                        g.DrawImage(baseLines[1], new PointF(0, tops[1]));
                    }
                }

                var rects = new RectangleF[n];
                for (var i = 0; i < n; i++)
                {
                    var h = (baseLines[i] ?? baseBitmap).Height;
                    rects[i] = new RectangleF(0, tops[i] + offsetY, baseBitmap.Width, h);
                    for (var k = 0; k < edges[i].Length; k++) edges[i][k] = Math.Min(edges[i][k], baseBitmap.Width);
                }

                Bitmap highlight = null;
                if (withHighlight)
                {
                    highlight = new Bitmap(baseBitmap.Width, baseBitmap.Height);
                    highlight.SetResolution(_dpi, _dpi);
                    using (var g = Graphics.FromImage(highlight))
                        for (var i = 0; i < n; i++) g.DrawImage(hiLines[i], new PointF(0, rects[i].Top));
                }
                return new RenderedBlock(baseBitmap, highlight, rects, edges, starts) { BoxRect = boxRect };
            }
            finally
            {
                foreach (var b in baseLines) b?.Dispose();
                if (hiLines != null) foreach (var b in hiLines) b?.Dispose();
            }
        }

        private static float BoxPadV => 14 * (_dpi / 96f);

        // One rounded box behind all lines, with extra room around the text for comfortable reading.
        private static Bitmap ComposeInBox(Bitmap[] lines, float[] tops, RectangleF[] textBounds, bool bakeBox, out RectangleF? boxRect)
        {
            boxRect = null;
            var scale = _dpi / 96f;
            var padH = 28 * scale;
            var padV = BoxPadV;
            var union = RectangleF.Empty;
            for (var i = 0; i < lines.Length; i++)
            {
                if (textBounds[i].Width <= 0) continue;
                var r = textBounds[i]; r.Offset(0, tops[i]);
                union = union.IsEmpty ? r : RectangleF.Union(union, r);
            }
            var contentHeight = 0f;
            for (var i = 0; i < lines.Length; i++) contentHeight = Math.Max(contentHeight, tops[i] + lines[i].Height);
            var width = lines.Max(b => b.Width);
            var bitmap = new Bitmap(width, (int)Math.Ceiling(contentHeight + padV * 2));
            bitmap.SetResolution(_dpi, _dpi);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_backgroundOpacity > 0 && !union.IsEmpty)
                {
                    var box = new RectangleF(union.Left - padH, union.Top, union.Width + padH * 2, union.Height + padV * 2);
                    box.Intersect(new RectangleF(0, 0, bitmap.Width, bitmap.Height));
                    boxRect = box;
                    if (bakeBox) FillBox(g, box);
                }
                for (var i = 0; i < lines.Length; i++) g.DrawImage(lines[i], new PointF(0, tops[i] + padV));
            }
            return bitmap;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Bitmap RenderLyrics(string lyric, Font font, IDeviceContext dc, bool secondLine,
                                           out RectangleF textBounds, out float[] charEdges, out float startX, bool highlightLayer)
        {
            charEdges = new float[lyric.Length];
            startX = 0;
            // TODO: bounds returned by `TextRenderer.MeasureTex` differs greatly
            //       from bounds created by `GraphicsPath.AddString`, need further
            //       investigation.
            var fontBounds = TextRenderer.MeasureText(dc, lyric, font);
            var height = fontBounds.Height;
            if (height <= 0) height = 1;
            var bitmap = new Bitmap(_width, height + BackgroundOffsetV * 2);
            bitmap.SetResolution(_dpi, _dpi);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.High;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.CompositingQuality = CompositingQuality.HighQuality;

                var fontEmSize = font.SizeInPoints * _dpi / 72;

                var initialRect = new RectangleF(0, BackgroundOffsetV, _width, height);
                using (var stringPath = new GraphicsPath(FillMode.Alternate))
                {
                    stringPath.AddString(lyric, font.FontFamily, (int)font.Style, fontEmSize, initialRect, Format);
                    var bounds = stringPath.GetBounds();
                    textBounds = bounds;
                    if (!highlightLayer) startX = MeasureCharEdges(g, lyric, font.FontFamily, font.Style, fontEmSize, initialRect, Format, charEdges);

                    if (highlightLayer)
                    {
                        // Text only, in the highlight colour: it is laid over the base text, which
                        // already carries the background box and the shadow.
                        if (_borderPen != null) g.DrawPath(_borderPen, stringPath);
                        using (var brush = new SolidBrush(_highlightColor)) g.FillPath(brush, stringPath);
                        return bitmap;
                    }

                    if (!_boxBackground && _backgroundOpacity > 0 && bounds.Width > 0)
                    {
                        using (var brush = new SolidBrush(Color.FromArgb(_backgroundOpacity, 0, 0, 0)))
                        {
                            g.FillRectangle(brush, new RectangleF(
                                bounds.Left - BackgroundOffsetH, 0,
                                bounds.Width + BackgroundOffsetH * 2, height + 30));
                        }
                    }

                    // Using matrix to translate the path
                    using (var mat = new Matrix())
                    {
                        mat.Translate(-ShadowOffset, -ShadowOffset);
                        stringPath.Transform(mat);

                        g.FillPath(ShadowBrush, stringPath);

                        mat.Translate(ShadowOffset * 2, ShadowOffset * 2);
                        stringPath.Transform(mat);
                    }


                    if (_borderPen != null)
                        g.DrawPath(_borderPen, stringPath);

                    using (var stringBrush = secondLine && _useLine2Color ? new SolidBrush(_line2Color) : CreateGradientBrush(initialRect))
                    {
                        g.FillPath(stringBrush, stringPath);
                    }
                }
            }
            
            return bitmap;
        }

        /// <summary>Right edge of every character, in the same layout as the GraphicsPath text (same
        /// format, rectangle and pixel em size). GDI+ measures at most 32 ranges per call.</summary>
        /// <summary>Also used by the karaoke window, with its own format. Returns the first character's left.</summary>
        internal static float MeasureCharEdges(Graphics g, string lyric, FontFamily family, FontStyle style, float emSizePx,
                                               RectangleF rect, StringFormat format, float[] edges)
        {
            if (lyric.Length == 0) return 0;
            var startX = float.NaN;
            using (var pxFont = new Font(family, emSizePx, style, GraphicsUnit.Pixel))
            using (var fmt = (StringFormat)format.Clone())
            {
                for (var start = 0; start < lyric.Length; start += 32)
                {
                    var count = Math.Min(32, lyric.Length - start);
                    fmt.SetMeasurableCharacterRanges(Enumerable.Range(start, count).Select(i => new CharacterRange(i, 1)).ToArray());
                    var regions = g.MeasureCharacterRanges(lyric, pxFont, rect, fmt);
                    for (var k = 0; k < regions.Length; k++)
                    {
                        var b = regions[k].GetBounds(g);
                        edges[start + k] = b.Width > 0 ? b.Right : float.NaN;   // e.g. the second half of a surrogate pair
                        if (float.IsNaN(startX) && b.Width > 0) startX = b.Left;
                        regions[k].Dispose();
                    }
                }
            }
            if (float.IsNaN(startX)) startX = 0;
            var prev = startX;
            for (var i = 0; i < edges.Length; i++)
            {
                if (float.IsNaN(edges[i]) || edges[i] < prev) edges[i] = prev;
                prev = edges[i];
            }
            return startX;
        }

        private static Brush CreateGradientBrush(RectangleF dstRect)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (_type)
            {
                case GradientType.DoubleColor:
                    return new LinearGradientBrush(dstRect.Location, new PointF(dstRect.X, dstRect.Y + dstRect.Height), _color1, _color2)
                    {
                        WrapMode = WrapMode.TileFlipXY
                    };
                case GradientType.TripleColor:
                    return new LinearGradientBrush(dstRect.Location, new PointF(dstRect.X, dstRect.Y + dstRect.Height / 3 * 2), _color1, _color2)
                    {
                        WrapMode = WrapMode.TileFlipXY
                    };
                case GradientType.NoGradient:
                default:
                    return new SolidBrush(_color1);
            }
        }
    }
}
