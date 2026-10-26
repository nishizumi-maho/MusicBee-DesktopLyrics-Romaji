using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MusicBeePlugin;
using Xunit;

namespace DesktopLyrics.Tests
{
    public class GdiLeakTests
    {
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();

        private sealed class LayeredForm : Form
        {
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000; return cp; }
            }
        }

        [Fact]
        public void PresentingFramesDoesNotLeakGdiObjects()
        {
            using (var form = new LayeredForm { Width = 400, Height = 100 })
            using (var frame = new Bitmap(400, 100, PixelFormat.Format32bppArgb))
            {
                var handle = form.Handle;
                GdiplusHelper.SetBitmap(frame, 255, handle, 400, 100);   // warm-up
                var before = GetGuiResources(GetCurrentProcess(), 0);
                for (var i = 0; i < 300; i++) GdiplusHelper.SetBitmap(frame, 255, handle, 400, 100);
                var after = GetGuiResources(GetCurrentProcess(), 0);
                Assert.True(after - before < 10, $"GDI objects grew by {after - before} over 300 frames");
            }
        }
    }
}
