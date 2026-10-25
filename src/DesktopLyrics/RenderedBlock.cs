// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): word-by-word highlight.
using System;
using System.Drawing;

namespace MusicBeePlugin
{
    /// <summary>A rendered lyrics block: the normal bitmap, an optional same-size highlight layer
    /// (text only, highlight colour), where each line sits, and each character's right edge.</summary>
    public sealed class RenderedBlock : IDisposable
    {
        public Bitmap Base { get; private set; }
        public Bitmap Highlight { get; private set; }
        public RectangleF[] LineRects { get; }
        public float[][] CharEdges { get; }
        /// <summary>Left edge of the first character of each line (lines are usually centred).</summary>
        public float[] LineStartX { get; }
        /// <summary>Where the single-box background goes (block coordinates); null when that option is off.</summary>
        public RectangleF? BoxRect { get; set; }

        public RenderedBlock(Bitmap @base, Bitmap highlight, RectangleF[] lineRects, float[][] charEdges, float[] lineStartX)
        {
            Base = @base;
            Highlight = highlight;
            LineRects = lineRects;
            CharEdges = charEdges;
            LineStartX = lineStartX;
        }

        /// <summary>x reached by a fractional number of lit characters on a line; 0 when nothing is lit.</summary>
        public float LitX(int line, double litChars)
        {
            var edges = CharEdges[line];
            if (litChars <= 0 || edges.Length == 0) return 0;
            if (litChars >= edges.Length) return edges[edges.Length - 1];
            var i = (int)Math.Floor(litChars);
            var left = i == 0 ? LineStartX[line] : edges[i - 1];
            return left + (float)((litChars - i) * (edges[i] - left));
        }

        /// <summary>Hands the base bitmap to the caller (it will not be disposed with the block).</summary>
        public Bitmap DetachBase()
        {
            var b = Base;
            Base = null;
            return b;
        }

        public void Dispose()
        {
            Base?.Dispose();
            Highlight?.Dispose();
            Base = Highlight = null;
        }
    }
}
