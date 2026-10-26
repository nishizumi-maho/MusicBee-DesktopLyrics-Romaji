// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): karaoke window.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    /// <summary>A normal, resizable window with the whole song (see <see cref="ScrollingLyricsForm"/>).
    /// F11 or a double-click toggles fullscreen; Esc leaves it.</summary>
    public sealed class FrmKaraoke : ScrollingLyricsForm
    {
        private bool _fullscreen;
        private Rectangle _restoreBounds;

        public FrmKaraoke(SettingsObj settings) : base(settings)
        {
            Text = "Desktop Lyrics — Karaoke";
            ForeColor = Current;
            KeyPreview = true;
            MinimumSize = new Size(360, 240);

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

            Shown += (s, e) => { if (Settings.KaraokeFullscreen) SetFullscreen(true); };
            DoubleClick += (s, e) => SetFullscreen(!_fullscreen);
            KeyDown += OnKeyDown;
            FormClosing += (s, e) =>
            {
                FrameTimer.Stop();
                var b = _fullscreen ? _restoreBounds : (WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
                Settings.KaraokeX = b.X;
                Settings.KaraokeY = b.Y;
                Settings.KaraokeW = b.Width;
                Settings.KaraokeH = b.Height;
                Settings.KaraokeFullscreen = _fullscreen;
            };
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
            InvalidateLayout();
        }
    }
}
