// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): move tab.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class FrmLyrics
    {
        // ---- move tab ----
        //
        // A small handle above the lyrics. Dragging it moves the lyrics window, so the lyrics can be moved
        // without grabbing the text itself (which is hard to hit and keeps changing under the cursor).
        // Optional, from the right-click menu; it shows and hides together with the lyrics.
        private MoveTab _moveTab;
        private bool _placingTab;
        private bool _tabBelow;                 // no room above the lyrics: the tab sits on their top edge

        private void SetMoveTab(bool on)
        {
            _settings.ShowMoveTab = on;
            ApplyMoveTab();
            SettingsEdited?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyMoveTab()
        {
            var show = _settings.ShowMoveTab && Visible;
            if (!show)
            {
                _moveTab?.Hide();
                return;
            }
            if (_moveTab == null || _moveTab.IsDisposed)
            {
                _moveTab = new MoveTab(DeviceDpi) { Owner = this };
                _moveTab.Move += (s, e) => FollowTab();
                _moveTab.HideRequested += (s, e) => SetMoveTab(false);
            }
            PlaceTab();
            if (!_moveTab.Visible) _moveTab.Show();
        }

        /// <summary>Puts the tab centred over the top edge of the lyrics; below it when there is no room above.</summary>
        private void PlaceTab()
        {
            if (_moveTab == null || _moveTab.IsDisposed) return;
            var screen = Screen.FromControl(this).Bounds;
            var x = Left + (Width - _moveTab.Width) / 2;
            _tabBelow = Top - _moveTab.Height < screen.Top;
            var y = _tabBelow ? Top : Top - _moveTab.Height;
            _placingTab = true;
            try { _moveTab.Location = new Point(x, y); }
            finally { _placingTab = false; }
        }

        /// <summary>The tab was dragged: bring the lyrics along.</summary>
        private void FollowTab()
        {
            if (_placingTab) return;
            var left = _moveTab.Left - (Width - _moveTab.Width) / 2;
            var top = _tabBelow ? _moveTab.Top : _moveTab.Bottom;
            _placingTab = true;
            try { Location = new Point(left, top); }
            finally { _placingTab = false; }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (!_placingTab) PlaceTab();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_placingTab) PlaceTab();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            ApplyMoveTab();
        }

        private sealed class MoveTab : Form
        {
            public event EventHandler HideRequested;

            public MoveTab(int dpi)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.FromArgb(40, 40, 40);
                Opacity = 0.8;
                Cursor = Cursors.SizeAll;
                DoubleBuffered = true;
                var scale = dpi / 96f;
                Size = new Size((int)(64 * scale), (int)(16 * scale));
                var tip = new ToolTip();
                tip.SetToolTip(this, "Drag to move the lyrics (right-click to hide)");
                Disposed += (s, e) => tip.Dispose();
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                    cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    return cp;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // Grip: two rows of dots.
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var d = Math.Max(2f, Height / 6f);
                var gap = d * 2;
                const int dots = 6;
                var x0 = (Width - (dots * d + (dots - 1) * (gap - d))) / 2f;
                using (var brush = new SolidBrush(Color.FromArgb(200, 200, 200)))
                    for (var row = 0; row < 2; row++)
                    for (var i = 0; i < dots; i++)
                        g.FillEllipse(brush, x0 + i * gap, Height / 2f - d * 1.25f + row * d * 1.5f, d, d);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Right)
                {
                    HideRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }
                if (e.Button != MouseButtons.Left) return;
                Unmanaged.Unmanaged.ReleaseCapture();
                Unmanaged.Unmanaged.SendMessage(Handle, 0x00A1, new IntPtr(0x0002), null);   // drag as if by the caption
            }
        }
    }
}
