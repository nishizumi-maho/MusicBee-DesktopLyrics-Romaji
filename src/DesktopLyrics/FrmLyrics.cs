// Modified by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): Lyrics opacity (whole window alpha), a right-click menu with settings and opacity presets,
// romaji switch and update check in that menu, and smooth-scroll line transitions.
// Original work: Copyright Charlie Jiang, licensed under the Apache License 2.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Unmanaged;
using Timer = System.Windows.Forms.Timer;   // System.Threading is imported for Interlocked; it also has a Timer

namespace MusicBeePlugin
{
    public partial class FrmLyrics : Form
    {
        private SettingsObj _settings;

        // Public entry points are called from the plugin's timer and from MusicBee notification
        // threads. Every bitmap here belongs to the UI thread, so those calls are re-posted to it;
        // touching the blocks from two threads is what raised "Object is currently in use elsewhere".
        private bool PostToUiThread(Action action)
        {
            if (!InvokeRequired) return false;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }   // handle not created yet, or the form is closing
            return true;
        }

        public FrmLyrics(SettingsObj settings)
        {
            _settings = settings;

            InitializeComponent();

            _animTimer = new Timer { Interval = FrameIntervalMs };
            _animTimer.Tick += OnAnimTick;          // fallback path only; normally idle
            _mmCallback = OnMmTimerTick;            // rooted here so winmm can never call a collected stub
            _animWatch = new Stopwatch();
        }

        // ---- Smooth-scroll transitions ----
        //
        // A rendered "block" is one bitmap with the box/background already baked in by LyricsRenderer.
        // While a transition runs we never re-render text; we only translate the two cached blocks and
        // recompose them into a single frame, so the per-frame cost is one bitmap alloc + two blits.
        private const int DefaultFps = 60;
        private const int AnimMinDuration = 200;
        private const int AnimMaxDuration = 1500;

        private readonly Timer _animTimer;
        private readonly Stopwatch _animWatch;
        private Bitmap _blockCurrent;              // block shown at rest; also the outgoing block mid-transition
        private Bitmap _blockIncoming;             // block sliding in; null while at rest
        private bool _animating;
        private int _animTravel;
        private int _animDuration = 500;
        private bool _incomingHasContent;

        private int FrameIntervalMs
        {
            get
            {
                var fps = _settings == null ? DefaultFps : _settings.SmoothScrollFps;
                var maxHz = MonitorInfo.PrimaryRefreshHz();   // never out-run the display
                if (fps < 10) fps = 10;
                if (fps > maxHz) fps = maxHz;
                if (fps > 1000) fps = 1000;
                return Math.Max(1, 1000 / fps);
            }
        }

        // ---- animation clock ----
        //
        // Driven by a winmm multimedia timer rather than System.Windows.Forms.Timer. That timer is
        // built on WM_TIMER, which Windows quantises to the ~15.6 ms system tick: measured
        // Interval = 33 actually fires every 46.8 ms (~21 fps), Interval = 4 lands on 15.6 ms, and
        // timeBeginPeriod(1) does not lift it (Sleep improves, SetTimer does not). Every rate the
        // slider offers collapses onto that ceiling without this swap.
        //
        // timeSetEvent was measured at 33 -> 30.3 fps, 16 -> 62.0, 8 -> 125.1, 4 -> 252.2, so it can
        // honour a slider that goes to the monitor's refresh rate.
        //
        // Its callback arrives on a winmm worker thread, so frames are marshalled to the UI thread
        // with BeginInvoke and coalesced: while a frame is queued, further callbacks are dropped.
        // A UI thread that stalls therefore drains its backlog instead of accumulating one.
        private uint _mmTimerId;
        private int _framePending;                         // 0 = idle, 1 = a frame is queued
        private volatile bool _clockStopped;
        private readonly Unmanaged.Unmanaged.TimeProc _mmCallback;

        private void StartAnimationClock()
        {
            StopAnimationClock();

            _clockStopped = false;
            _mmTimerId = Unmanaged.Unmanaged.timeSetEvent(
                (uint)FrameIntervalMs, 1, _mmCallback, IntPtr.Zero, Unmanaged.Unmanaged.TIME_PERIODIC);
            if (_mmTimerId != 0) return;

            // winmm refused the timer: fall back to the WinForms timer. It is quantised to the system
            // tick so the animation will be coarse, but it still runs instead of freezing.
            _animTimer.Interval = FrameIntervalMs;
            _animTimer.Start();
        }

        private void StopAnimationClock()
        {
            // Set before killing: a callback already in flight must see this and return, not queue work
            // against a form that is about to change state.
            _clockStopped = true;
            if (_mmTimerId != 0)
            {
                Unmanaged.Unmanaged.timeKillEvent(_mmTimerId);
                _mmTimerId = 0;
            }
            _animTimer.Stop();
            Interlocked.Exchange(ref _framePending, 0);
        }

        private void OnMmTimerTick(uint id, uint msg, IntPtr user, IntPtr arg1, IntPtr arg2)
        {
            if (_clockStopped) return;
            if (Interlocked.Exchange(ref _framePending, 1) != 0) return;   // a frame is already queued

            var queued = false;
            try
            {
                BeginInvoke((Action)OnQueuedAnimTick);
                queued = true;
            }
            catch (InvalidOperationException) { }   // window handle not created yet, or form disposed
            if (!queued) Interlocked.Exchange(ref _framePending, 0);
        }

        private void OnQueuedAnimTick()
        {
            // Cleared before rendering, so a callback arriving mid-frame queues the *next* one.
            Interlocked.Exchange(ref _framePending, 0);
            OnAnimTick(this, EventArgs.Empty);
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            if (!_animating)
            {
                // At rest the same clock drives the word highlight, if there is one to drive.
                if (!_highlightOn || PresentHighlightFrame() || !_clock.Playing) StopAnimationClock();
                return;
            }
            if (_blockCurrent == null || _blockIncoming == null)
            {
                StopAnimation();
                return;
            }

            var t = _animDuration <= 0 ? 1.0 : _animWatch.Elapsed.TotalMilliseconds / _animDuration;
            if (t >= 1.0)
            {
                FinishTransition();
                return;
            }

            // Ease-out cubic: leaves quickly, settles gently, like paper coming to rest.
            var p = 1.0 - Math.Pow(1.0 - t, 3.0);
            PresentFrame(_blockCurrent, (int)Math.Round(-_animTravel * p),
                         _blockIncoming, (int)Math.Round(_animTravel * (1.0 - p)),
                         _animTravel);
        }

        private void FinishTransition()
        {
            StopAnimationClock();
            _animating = false;
            _blockCurrent?.Dispose();
            _blockCurrent = _blockIncoming;
            _blockIncoming = null;
            ReplaceWordBlock(_wordBlockIncoming);
            _wordBlockIncoming = null;
            _hasContent = _incomingHasContent;
            if (_blockCurrent != null)
                PresentFrame(null, 0, _blockCurrent, 0, _blockCurrent.Height);
            ApplyVisibility();
            StartHighlight();
        }

        private void StopAnimation()
        {
            StopAnimationClock();
            _animating = false;
            _blockIncoming?.Dispose();
            _blockIncoming = null;
            _wordBlockIncoming?.Dispose();
            _wordBlockIncoming = null;
            _animWatch.Reset();
        }

        /// <summary>Folds whatever is on screen right now into a single bitmap, so an interrupted
        /// transition can hand over to a new one without the lyrics jumping back to their rest position.</summary>
        private Bitmap SnapshotCurrentFrame()
        {
            if (_blockCurrent == null) return null;
            if (!_animating || _blockIncoming == null || _animTravel <= 0)
                return new Bitmap(_blockCurrent);

            var t = _animDuration <= 0 ? 1.0 : _animWatch.Elapsed.TotalMilliseconds / _animDuration;
            var p = Math.Min(1.0, 1.0 - Math.Pow(1.0 - t, 3.0));
            var snap = new Bitmap(_blockCurrent.Width, _animTravel);
            using (var g = Graphics.FromImage(snap))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.DrawImage(_blockCurrent, 0, (int)Math.Round(-_animTravel * p), _blockCurrent.Width, _blockCurrent.Height);
                g.DrawImage(_blockIncoming, 0, (int)Math.Round(_animTravel * (1.0 - p)), _blockIncoming.Width, _blockIncoming.Height);
            }
            return snap;
        }

        /// <summary>Pushes one composed frame to the layered window. Blocks are always full width,
        /// so they only need a Y offset; sizes are passed explicitly to keep the blit pixel-exact
        /// regardless of the DPI the blocks were rendered at.</summary>
        private void PresentFrame(Bitmap outgoing, int outgoingY, Bitmap incoming, int incomingY, int viewHeight)
        {
            var width = (incoming ?? outgoing)?.Width ?? 0;
            if (width <= 0 || viewHeight <= 0) return;

            using (var frame = new Bitmap(width, viewHeight))
            {
                using (var g = Graphics.FromImage(frame))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    if (outgoing != null) g.DrawImage(outgoing, 0, outgoingY, outgoing.Width, outgoing.Height);
                    if (incoming != null) g.DrawImage(incoming, 0, incomingY, incoming.Width, incoming.Height);
                }

                if (Width != width) Width = width;
                if (Height != viewHeight) Height = viewHeight;

                GdiplusHelper.SetBitmap(frame, TextAlpha, Handle, Left, Top, Width, Height);
            }
        }

        private void FrmLyrics_Load(object sender, EventArgs e)
        {
            // TODO: multi-screen support
            var scrBounds = Screen.PrimaryScreen.Bounds;
            LyricsRenderer.SetDpi(DeviceDpi);
            Width = scrBounds.Width;
            Height = 150;
            if (_settings.PosY < 0)
            {
                Top = scrBounds.Height - Height - 150;
                Left = 0;
            }
            else
            {
                Top = _settings.PosY;
                Left = _settings.PosX;
            }

            TopMost = true;

            UnmanagedHelper.SetTopMost(this);
            UpdateFromSettings(_settings);

            Move += (o, args) =>
            {
                _settings.PosX = Left;
                _settings.PosY = Top;
            };
            FormClosing += (o, args) =>
            {
                if (args.CloseReason == CloseReason.UserClosing)
                {
                    args.Cancel = true;
                    return;
                }
                // Real shutdown (plugin closed / host exiting): release the clock, blocks and timer.
                StopAnimationClock();
                _animTimer.Dispose();
                _blockCurrent?.Dispose();
                _blockCurrent = null;
                _blockIncoming?.Dispose();
                _blockIncoming = null;
                ReplaceWordBlock(null);
                _wordBlockIncoming?.Dispose();
                _wordBlockIncoming = null;
            };
        }

        /// <summary>Raised after the opacity is changed from the right-click menu, so it can be saved.</summary>
        public event EventHandler SettingsEdited;

        private void SetOpacity(int percent)
        {
            _settings.TextOpacity = percent;
            // Text alpha is applied when the frame is pushed, not baked into the blocks, so there is
            // nothing to re-render.
            RepresentCurrent();
            SettingsEdited?.Invoke(this, EventArgs.Empty);
        }

        private void RepresentCurrent()
        {
            if (_animating) return;   // an in-flight frame already carries the new alpha
            if (_blockCurrent == null)
            {
                Redraw();
                return;
            }
            PresentFrame(null, 0, _blockCurrent, 0, _blockCurrent.Height);
            if (_highlightOn)
            {
                _lastX1 = _lastX2 = -1;   // the plain frame just replaced the highlight: paint it again
                PresentHighlightFrame();
            }
        }

        public void UpdateFromSettings(SettingsObj settings)
        {
            if (PostToUiThread(() => UpdateFromSettings(settings))) return;
            _settings = settings;
            LyricsRenderer.UpdateFromSettings(settings, Width);

            // Every cached block was rendered with the previous settings.
            Redraw();
        }

        // The lyrics window is a fully transparent layered window: when there is nothing to draw it must
        // actually go away, otherwise it sits on the desktop looking like the plugin never opened.
        private bool _hasContent;
        private bool _playStateAllows = true;

        /// <summary>Called when the player stops/starts, honouring the "Hide lyrics when stopped" option.</summary>
        public void SetPlayStateVisible(bool visible)
        {
            if (PostToUiThread(() => SetPlayStateVisible(visible))) return;
            _playStateAllows = visible || !_settings.AutoHide;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            var shouldShow = _hasContent && _playStateAllows;
            if (Visible != shouldShow) Visible = shouldShow;
        }

        private string _line1, _line2;

        /// <summary>Full re-render at rest. Used when settings change; never animates.</summary>
        private void Redraw()
        {
            StopAnimation();
            var block = RenderBlock(out var word);
            if (block == null) return;

            _blockCurrent?.Dispose();
            _blockCurrent = block;
            ReplaceWordBlock(word);
            _hasContent = HasContent();

            PresentFrame(null, 0, block, 0, block.Height);
            ApplyVisibility();
            StartHighlight();
        }

        public void Clear()
        {
            if (PostToUiThread(Clear)) return;
            if (_line1 == null && _line2 == null) return;
            if (string.IsNullOrEmpty(_line1) && _line2 == null) return;
            _line1 = "";
            _line2 = null;
            _segs1 = _segs2 = null;
            TransitionToNewContent();
        }

        public void UpdateLyrics(LyricsController.LyricView view)
        {
            if (PostToUiThread(() => UpdateLyrics(view))) return;
            // The plugin force-refreshes on play-state changes; re-animating identical content would
            // make the lyrics jump for no reason.
            if (view.LyricLine1 == _line1 && view.LyricLine2 == _line2 && view.Segments == _segs1) return;
            _line1 = view.LyricLine1;
            _line2 = view.LyricLine2;
            _segs1 = view.Segments;
            _segs2 = view.Segments2;
            _offsetMs = view.OffsetMs;
            TransitionToNewContent();
        }

        private bool HasContent() => !string.IsNullOrEmpty(_line1) || !string.IsNullOrEmpty(_line2);

        /// <summary>Renders the block for the current lines. When the line has word timing and the
        /// highlight is on, <paramref name="word"/> receives the highlight layer and character edges.</summary>
        private Bitmap RenderBlock(out RenderedBlock word)
        {
            var withHighlight = _settings.WordHighlight && _segs1 != null && !string.IsNullOrEmpty(_line1);
            using (var g = CreateGraphics())
            {
                var block = _line2 == null
                    ? LyricsRenderer.Render1LineBlock(_line1 ?? "", g, withHighlight)
                    : LyricsRenderer.Render2LineBlock(_line1 ?? "", _line2, g, withHighlight);
                var bitmap = block.DetachBase();
                if (withHighlight)
                {
                    word = block;
                }
                else
                {
                    word = null;
                    block.Dispose();
                }
                return bitmap;
            }
        }

        private void TransitionToNewContent()
        {
            var incomingHasContent = HasContent();
            var incoming = RenderBlock(out var word);
            if (incoming == null) return;

            // Nothing to slide from, or the option is off: paint the new block straight away.
            if (!_settings.SmoothScroll || _blockCurrent == null)
            {
                StopAnimation();
                _blockCurrent?.Dispose();
                _blockCurrent = incoming;
                ReplaceWordBlock(word);
                _hasContent = incomingHasContent;
                PresentFrame(null, 0, incoming, 0, incoming.Height);
                ApplyVisibility();
                StartHighlight();
                return;
            }

            // A transition is already running: hand over from what the user actually sees on screen,
            // not from the stale rest-state block.
            if (_animating && _blockIncoming != null)
            {
                var snapshot = SnapshotCurrentFrame();
                if (snapshot != null)
                {
                    _blockCurrent?.Dispose();
                    _blockCurrent = snapshot;
                }
            }
            StopAnimation();

            // The outgoing line slides away plain; its highlight layer is no longer needed.
            ReplaceWordBlock(null);
            _blockIncoming = incoming;
            _wordBlockIncoming = word;
            _incomingHasContent = incomingHasContent;
            _animTravel = Math.Max(_blockCurrent.Height, incoming.Height);
            _animDuration = Math.Max(AnimMinDuration, Math.Min(AnimMaxDuration, _settings.SmoothScrollDuration));
            _animWatch.Restart();

            // Keep the window up for the whole transition even when the destination is empty,
            // otherwise the outgoing line would vanish instead of sliding away.
            _hasContent = true;
            ApplyVisibility();

            // Set _animating before the clock: the first multimedia callback can arrive immediately,
            // and OnAnimTick would otherwise treat it as a dead transition and stop it.
            _animating = true;
            StartAnimationClock();
            OnAnimTick(this, EventArgs.Empty);   // paint the first frame without waiting for a callback
        }

        // ---- word-by-word highlight ----
        //
        // When the line on screen has word timing, _wordBlock holds a same-size layer with the text in
        // the highlight colour plus every character's x. Each frame lays that layer over the plain block,
        // clipped at the x the singer has reached, so the colour runs through each word as it is sung.
        // The multimedia clock that drives transitions also drives this; it stops when the line is fully
        // lit or playback pauses, so a still line costs nothing.
        private List<LyricParser.WordSegment> _segs1, _segs2;
        private double _offsetMs;
        private RenderedBlock _wordBlock, _wordBlockIncoming;
        private readonly PlaybackClock _clock = new PlaybackClock();
        private bool _highlightOn;
        private float _lastX1 = -1, _lastX2 = -1;

        private void ReplaceWordBlock(RenderedBlock word)
        {
            if (!ReferenceEquals(_wordBlock, word)) _wordBlock?.Dispose();
            _wordBlock = word;
            _highlightOn = false;
        }

        private void StartHighlight()
        {
            _lastX1 = _lastX2 = -1;
            _highlightOn = _wordBlock != null && _settings.WordHighlight && _blockCurrent != null;
            if (!_highlightOn) return;
            var done = PresentHighlightFrame();
            if (!done && _clock.Playing) StartAnimationClock();
        }

        /// <summary>Called every 100 ms with MusicBee's position. Pause, play and seeks show at once.</summary>
        public void SyncPlayback(double positionMs, bool playing)
        {
            if (PostToUiThread(() => SyncPlayback(positionMs, playing))) return;
            var wasPlaying = _clock.Playing;
            _clock.Sync(positionMs, playing);
            if (!_highlightOn || _animating) return;
            var done = PresentHighlightFrame();
            if (playing && !done && (!wasPlaying || _mmTimerId == 0 && !_animTimer.Enabled)) StartAnimationClock();
            else if (!playing || done) StopAnimationClock();
        }

        /// <summary>Paints the highlight for the current playback time. Returns true when every word of
        /// the line is lit (nothing left to animate).</summary>
        private bool PresentHighlightFrame()
        {
            if (_wordBlock?.Highlight == null || _blockCurrent == null || _segs1 == null) return true;
            var t = _clock.NowMs() + _offsetMs;
            var lit1 = WordTimeline.LitChars(_segs1, t);
            var x1 = _wordBlock.LitX(0, lit1);
            var done = lit1 >= _wordBlock.CharEdges[0].Length;
            var x2 = 0f;
            if (_segs2 != null && _wordBlock.CharEdges.Length > 1)
            {
                var lit2 = WordTimeline.LitChars(_segs2, t);
                x2 = _wordBlock.LitX(1, lit2);
                done &= lit2 >= _wordBlock.CharEdges[1].Length;
            }
            if (Math.Abs(x1 - _lastX1) < 0.5f && Math.Abs(x2 - _lastX2) < 0.5f) return done;
            _lastX1 = x1;
            _lastX2 = x2;

            var w = _blockCurrent.Width;
            var h = _blockCurrent.Height;
            using (var frame = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(frame))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(_blockCurrent, 0, 0, w, h);
                    DrawLit(g, 0, x1, w, h);
                    if (x2 > 0) DrawLit(g, 1, x2, w, h);
                }
                if (Width != w) Width = w;
                if (Height != h) Height = h;
                GdiplusHelper.SetBitmap(frame, TextAlpha, Handle, Left, Top, Width, Height);
            }
            return done;
        }

        private void DrawLit(Graphics g, int line, float x, int w, int h)
        {
            if (x <= 0) return;
            var r = _wordBlock.LineRects[line];
            g.SetClip(new RectangleF(0, r.Top, x, r.Height));
            g.DrawImage(_wordBlock.Highlight, 0, 0, w, h);
            g.ResetClip();
        }

        private byte TextAlpha => (byte)(Math.Max(10, Math.Min(100, _settings.TextOpacity)) * 255 / 100);

        /// <summary>Raised when the user right-clicks the lyrics and picks "Desktop Lyrics settings...".</summary>
        public event EventHandler SettingsRequested;

        /// <summary>Current romaji switch for the right-click menu; null hides the item.</summary>
        public Func<bool?> RomajiStateProvider { get; set; }

        /// <summary>Raised with the new state when the user toggles "Show romaji".</summary>
        public event EventHandler<bool> RomajiToggled;

        /// <summary>Raised when the user picks "Check for updates..." from the right-click menu.</summary>
        public event EventHandler UpdateCheckRequested;

        protected override void OnHandleDestroyed(EventArgs e)
        {
            // Dispose() skips FormClosing, so the multimedia timer must also be stopped here.
            StopAnimationClock();
            base.OnHandleDestroyed(e);
        }

        private void FrmLyrics_MouseDown(object sender, MouseEventArgs e)
        {
            if (!Visible) return;
            Unmanaged.Unmanaged.ReleaseCapture();
            if (e.Button == MouseButtons.Right)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("Desktop Lyrics settings...", null, (s, a) => SettingsRequested?.Invoke(this, EventArgs.Empty));
                var romaji = RomajiStateProvider?.Invoke();
                if (romaji.HasValue)
                {
                    var item = new ToolStripMenuItem("Show romaji (Japanese lyrics)") { Checked = romaji.Value };
                    item.Click += (s, a) => RomajiToggled?.Invoke(this, !romaji.Value);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Opacity" + ": 100%", null, (s, a) => SetOpacity(100));
                menu.Items.Add("Opacity" + ": 75%", null, (s, a) => SetOpacity(75));
                menu.Items.Add("Opacity" + ": 50%", null, (s, a) => SetOpacity(50));
                menu.Items.Add("Opacity" + ": 30%", null, (s, a) => SetOpacity(30));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Check for updates...", null, (s, a) => UpdateCheckRequested?.Invoke(this, EventArgs.Empty));
                menu.Closed += (s, a) => BeginInvoke(new Action(menu.Dispose));
                menu.Show(this, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            Unmanaged.Unmanaged.SendMessage(Handle, 0x00A1, new IntPtr(0x0002), null);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }
    }
}
