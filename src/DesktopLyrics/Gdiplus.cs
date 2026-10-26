using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Serialization;
using Gdiplus;
using Point = Gdiplus.Point;
using Size = Gdiplus.Size;

namespace MusicBeePlugin
{
    public static class GdiplusHelper
    {
        public static IntPtr GetHBitmap(Bitmap bitmap)
        {
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ApplicationException("The bitmap must be 32ppp with alpha-channel.");
            return bitmap.GetHbitmap(Color.FromArgb(0));
        }

        public static void ReleaseHBitmap(IntPtr hBitmap)
        {
            if (hBitmap != IntPtr.Zero) GdiplusPInvoke.DeleteObject(hBitmap);
        }

        /// <summary>Pushes a frame to a layered window. The position is left alone: passing the form's
        /// Left/Top here fought a window drag, snapping the window back to where it was a frame earlier.</summary>
        public static void SetBitmap(Bitmap bitmap, byte opacity, IntPtr handle, int width, int height)
        {
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ApplicationException("The bitmap must be 32ppp with alpha-channel.");

            var screenDc = GdiplusPInvoke.GetDC(IntPtr.Zero);
            var memDc = GdiplusPInvoke.CreateCompatibleDC(screenDc);
            var oldBitmap = IntPtr.Zero;
            var hBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = GdiplusPInvoke.SelectObject(memDc, hBitmap);

                var size = new Size(width, height);
                var pointSource = new Point(0, 0);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = GdiplusPInvoke.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = opacity,
                    AlphaFormat = GdiplusPInvoke.AC_SRC_ALPHA
                };

                GdiplusPInvoke.UpdateLayeredWindow(
                    handle,
                    screenDc, 
                    IntPtr.Zero,
                    ref size, 
                    memDc, 
                    ref pointSource, 
                    0, 
                    ref blend, 
                    GdiplusPInvoke.ULW_ALPHA);
            }
            finally
            {
                GdiplusPInvoke.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    // Deselect before deleting: DeleteObject fails on a bitmap still selected into a DC,
                    // which leaked one GDI bitmap per frame and eventually broke GDI+ in the whole host.
                    if (oldBitmap != IntPtr.Zero) GdiplusPInvoke.SelectObject(memDc, oldBitmap);
                    GdiplusPInvoke.DeleteObject(hBitmap);
                }
                GdiplusPInvoke.DeleteDC(memDc);
            }
        }
    }

    public class GdiplusException : Exception
    {
        public GdiplusException()
        {
        }

        public GdiplusException(string message) : base(message)
        {
        }

        public GdiplusException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected GdiplusException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
