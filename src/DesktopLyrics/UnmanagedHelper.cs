using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;

namespace Unmanaged
{
    public static class UnmanagedHelper
    {
        public static void SetTopMost(Form form)
        {
            // new IntPtr(-1) = HWND_TOPMOST
            if (!Unmanaged.SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0,
                Unmanaged.SetWindowPosFlags.IgnoreMove | Unmanaged.SetWindowPosFlags.IgnoreResize))
                throw new UnmanagedException($"Failed executing SetWindowPos, last error {Marshal.GetLastWin32Error()}");
        }
    }

    public static class Unmanaged
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", SetLastError=true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SetWindowPosFlags uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        internal static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

        /// <summary>
        /// winmm multimedia timer. The lyrics animation uses this instead of <c>System.Windows.Forms.Timer</c>
        /// because that timer is built on WM_TIMER, which Windows quantises to the ~15.6 ms system tick.
        /// Measured on this machine: a WinForms timer with <c>Interval = 33</c> actually fires every 46.8 ms
        /// (~21 fps), and raising the tick with <c>timeBeginPeriod</c> does not lift that ceiling -- every
        /// rate above 60 fps collapses onto it. <see cref="timeSetEvent"/> was measured delivering
        /// 30/60/125/240 fps exactly (4 ms period -> 252 fps), so it is the only one of the two that can
        /// honour a refresh-rate slider. Its callback arrives on a winmm worker thread, so callers must
        /// marshal to the UI thread themselves.
        /// </summary>
        [DllImport("winmm.dll")]
        internal static extern uint timeSetEvent(uint delay, uint resolution, TimeProc callback, IntPtr user, uint flags);

        [DllImport("winmm.dll")]
        internal static extern uint timeKillEvent(uint eventId);

        /// <summary>Callback for <see cref="timeSetEvent"/>; mirrors winmm's TIMECALLBACK
        /// (DWORD_PTR arguments, hence IntPtr rather than uint).</summary>
        internal delegate void TimeProc(uint id, uint msg, IntPtr user, IntPtr arg1, IntPtr arg2);

        /// <summary>winmm timer type: repeat until killed. Passing 0 here yields a one-shot timer.</summary>
        internal const uint TIME_PERIODIC = 0x00000001;

        [DllImport("user32.dll")]
        internal static extern bool GetClientRect(IntPtr hwnd, ref RECT lpRect);

        [Flags]
        public enum SetWindowPosFlags : uint
        {
            AsynchronousWindowPosition = 0x4000,
            DeferErase = 0x2000,
            DrawFrame = 0x0020,
            FrameChanged = 0x0020,
            HideWindow = 0x0080,
            DoNotActivate = 0x0010,
            DoNotCopyBits = 0x0100,
            IgnoreMove = 0x0002,
            DoNotChangeOwnerZOrder = 0x0200,
            DoNotRedraw = 0x0008,
            DoNotReposition = 0x0200,
            DoNotSendChangingEvent = 0x0400,
            IgnoreResize = 0x0001,
            IgnoreZOrder = 0x0004,
            ShowWindow = 0x0040,
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        internal int left;
        internal int top;
        internal int right;
        internal int bottom;
    }

    /// <summary>Display-mode struct for <c>EnumDisplaySettings</c>. Laid out as DEVMODEA (156 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string dmDeviceName;
        internal short dmSpecVersion;
        internal short dmDriverVersion;
        internal short dmSize;
        internal short dmDriverExtra;
        internal int dmFields;
        internal int dmPositionX;
        internal int dmPositionY;
        internal int dmDisplayOrientation;
        internal int dmDisplayFixedOutput;
        internal short dmColor;
        internal short dmDuplex;
        internal short dmYResolution;
        internal short dmTTOption;
        internal short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string dmFormName;
        internal short dmLogPixels;
        internal int dmBitsPerPel;
        internal int dmPelsWidth;
        internal int dmPelsHeight;
        internal int dmDisplayFlags;
        internal int dmDisplayFrequency;
        internal int dmICMMethod;
        internal int dmICMIntent;
        internal int dmMediaType;
        internal int dmDitherType;
        internal int dmReserved1;
        internal int dmReserved2;
        internal int dmPanningWidth;
        internal int dmPanningHeight;
    }

    internal static class MonitorInfo
    {
        private const int EnumCurrentSettings = -1;

        /// <summary>Refresh rate of the primary monitor in Hz. Windows reports 1 on some configurations,
        /// so anything outside a plausible range falls back to 60.</summary>
        internal static int PrimaryRefreshHz()
        {
            var dm = new DevMode();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
            if (!Unmanaged.EnumDisplaySettings(null, EnumCurrentSettings, ref dm)) return 60;
            return dm.dmDisplayFrequency >= 24 && dm.dmDisplayFrequency <= 1000 ? dm.dmDisplayFrequency : 60;
        }
    }

    public enum Bool
    {
        False = 0,
        True
    };

    public class UnmanagedException : Exception
    {
        public UnmanagedException()
        {
        }

        public UnmanagedException(string message) : base(message)
        {
        }

        public UnmanagedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UnmanagedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
