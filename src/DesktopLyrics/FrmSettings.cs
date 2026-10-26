// Modified by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): New options: lyrics opacity, second-line colour, single box background, smooth line transitions (duration and frame rate), romaji switch, update options; dialog grows to fit.
// Original work: Copyright Charlie Jiang, licensed under the Apache License 2.0.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;
using Cyotek.Windows.Forms;
using Newtonsoft.Json;

namespace MusicBeePlugin
{
    [SuppressMessage("ReSharper", "LocalizableElement")]
    public partial class FrmSettings : Form
    {
        private Font _font = DefaultFont;
        private readonly SettingsObj _settings;
        private readonly ColorPickerDialog _colorDialog = new ColorPickerDialog();

        public event EventHandler<SettingsObj> SettingsChanged;

        /// <summary>Raised when the user clicks "Check now".</summary>
        public event EventHandler UpdateCheckRequested;

        private readonly Func<bool?> _getRomaji;
        private readonly Action<bool> _setRomaji;

        /// <param name="getRomaji">Current romaji switch, or null when the romaji engine is missing.</param>
        /// <param name="setRomaji">Stores a new romaji switch.</param>
        public FrmSettings(SettingsObj settings, Func<bool?> getRomaji = null, Action<bool> setRomaji = null)
        {
            _settings = settings;
            _getRomaji = getRomaji;
            _setRomaji = setRomaji;
            InitializeComponent();
            AddExtraOptions();
        }

        private TrackBar _barTextOpacity;
        private CheckBox _chkLine2Color;
        private Button _btnLine2Color;
        private CheckBox _chkBox;
        private CheckBox _chkFixedBox;
        private CheckBox _chkSmoothScroll;
        private CheckBox _chkContinuous;
        private TrackBar _barSmoothScroll;
        private Label _lblSmoothFps;
        private TrackBar _barSmoothFps;
        private CheckBox _chkRomaji;
        private CheckBox _chkCheckUpdates;
        private CheckBox _chkAutoInstall;
        private CheckBox _chkWordHighlight;
        private ComboBox _cmbLyricVersion;
        private Button _btnHighlightColor;

        // Text opacity and a separate colour for the second line (romaji / translation).
        private void AddExtraOptions()
        {
            var table = (TableLayoutPanel)Controls["tableLayout"];
            table.SuspendLayout();
            var row = table.RowCount;
            table.RowCount = row + 9;
            for (var i = 0; i < 9; i++) table.RowStyles.Add(new RowStyle());

            var lblBox = new Label { Text = "Background:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            _chkBox = new CheckBox { Text = "Single padded box (uses background opacity)", AutoSize = true, Checked = _settings.BoxBackground, Anchor = AnchorStyles.Left };
            _chkFixedBox = new CheckBox { Text = "Fixed size per song (largest line)", AutoSize = true, Checked = _settings.FixedBoxPerSong, Enabled = _settings.BoxBackground, Anchor = AnchorStyles.Left };
            _chkBox.CheckedChanged += (s, e) =>
            {
                _settings.BoxBackground = _chkBox.Checked;
                _chkFixedBox.Enabled = _chkBox.Checked;
                SettingsChanged?.Invoke(this, _settings);
            };
            _chkFixedBox.CheckedChanged += (s, e) => { _settings.FixedBoxPerSong = _chkFixedBox.Checked; SettingsChanged?.Invoke(this, _settings); };
            var boxFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            boxFlow.Controls.Add(_chkBox);
            boxFlow.Controls.Add(_chkFixedBox);
            table.Controls.Add(lblBox, 0, row + 2);
            table.Controls.Add(boxFlow, 1, row + 2);

            var lblOpacity = new Label { Text = "Lyrics opacity:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            _barTextOpacity = new TrackBar
            {
                Minimum = 10, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10,
                Dock = DockStyle.Fill, AutoSize = false, Height = barBackgroundOpacity.Height,
                Value = Math.Max(10, Math.Min(100, _settings.TextOpacity))
            };
            _barTextOpacity.Scroll += (s, e) => { _settings.TextOpacity = _barTextOpacity.Value; SettingsChanged?.Invoke(this, _settings); };

            var lblLine2 = new Label { Text = "2nd line colour (romaji):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _chkLine2Color = new CheckBox { Text = "Use own colour", AutoSize = true, Checked = _settings.UseLine2Color, Anchor = AnchorStyles.Left };
            _btnLine2Color = new Button { Width = btnColor1.Width / 2, Height = btnColor1.Height, BackColor = _settings.Line2Color, FlatStyle = btnColor1.FlatStyle, Enabled = _settings.UseLine2Color };
            _chkLine2Color.CheckedChanged += (s, e) =>
            {
                _settings.UseLine2Color = _chkLine2Color.Checked;
                _btnLine2Color.Enabled = _chkLine2Color.Checked;
                SettingsChanged?.Invoke(this, _settings);
            };
            _btnLine2Color.Click += (s, e) =>
            {
                _colorDialog.Color = _btnLine2Color.BackColor;
                var res = _colorDialog.ShowDialog();
                if (res != DialogResult.OK && res != DialogResult.Yes) return;
                _btnLine2Color.BackColor = _colorDialog.Color;
                _settings.Line2Color = _colorDialog.Color;
                SettingsChanged?.Invoke(this, _settings);
            };
            flow.Controls.Add(_chkLine2Color);
            flow.Controls.Add(_btnLine2Color);

            table.Controls.Add(lblOpacity, 0, row);
            table.Controls.Add(_barTextOpacity, 1, row);
            table.Controls.Add(lblLine2, 0, row + 1);
            table.Controls.Add(flow, 1, row + 1);

            var lblSmooth = new Label { Text = "Transitions:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            _chkSmoothScroll = new CheckBox { Text = "Smooth line transitions (slide up)", AutoSize = true, Checked = _settings.SmoothScroll, Anchor = AnchorStyles.Left };
            _chkSmoothScroll.CheckedChanged += (s, e) =>
            {
                _settings.SmoothScroll = _chkSmoothScroll.Checked;
                _barSmoothScroll.Enabled = _chkSmoothScroll.Checked;
                _barSmoothFps.Enabled = _chkSmoothScroll.Checked;
                SettingsChanged?.Invoke(this, _settings);
            };
            _chkContinuous = new CheckBox { Text = "Continuous mode (2 lines rolling up)", AutoSize = true, Checked = _settings.ContinuousMode, Anchor = AnchorStyles.Left };
            _chkContinuous.CheckedChanged += (s, e) => { _settings.ContinuousMode = _chkContinuous.Checked; SettingsChanged?.Invoke(this, _settings); };
            var smoothFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            smoothFlow.Controls.Add(_chkSmoothScroll);
            smoothFlow.Controls.Add(_chkContinuous);
            table.Controls.Add(lblSmooth, 0, row + 3);
            table.Controls.Add(smoothFlow, 1, row + 3);

            var lblSmoothDur = new Label { Text = "Transition time:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            _barSmoothScroll = new TrackBar
            {
                Minimum = 200, Maximum = 1500, TickFrequency = 100, SmallChange = 50, LargeChange = 200,
                Dock = DockStyle.Fill, AutoSize = false, Height = barBackgroundOpacity.Height,
                Value = Math.Max(200, Math.Min(1500, _settings.SmoothScrollDuration)),
                Enabled = _settings.SmoothScroll
            };
            _barSmoothScroll.Scroll += (s, e) => { _settings.SmoothScrollDuration = _barSmoothScroll.Value; SettingsChanged?.Invoke(this, _settings); };
            table.Controls.Add(lblSmoothDur, 0, row + 4);
            table.Controls.Add(_barSmoothScroll, 1, row + 4);

            // Frame rate: capped at what the monitor can actually show, because pushing frames faster
            // than the refresh rate just makes DWM throw them away.
            var refreshHz = Unmanaged.MonitorInfo.PrimaryRefreshHz();
            var minFps = Math.Min(30, refreshHz);
            _lblSmoothFps = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            _barSmoothFps = new TrackBar
            {
                Minimum = minFps, Maximum = refreshHz,
                TickFrequency = Math.Max(10, (refreshHz - minFps) / 8), SmallChange = 10, LargeChange = 30,
                Dock = DockStyle.Fill, AutoSize = false, Height = barBackgroundOpacity.Height,
                Value = Math.Max(minFps, Math.Min(refreshHz, _settings.SmoothScrollFps)),
                Enabled = _settings.SmoothScroll
            };
            void LabelFps() => _lblSmoothFps.Text = "Frame rate: " + _barSmoothFps.Value + " fps (monitor " + refreshHz + " Hz)";
            _barSmoothFps.Scroll += (s, e) => { _settings.SmoothScrollFps = _barSmoothFps.Value; LabelFps(); SettingsChanged?.Invoke(this, _settings); };
            LabelFps();
            table.Controls.Add(_lblSmoothFps, 0, row + 5);
            table.Controls.Add(_barSmoothFps, 1, row + 5);

            var romaji = _getRomaji?.Invoke();
            var lblRomaji = new Label { Text = "Romaji:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            _chkRomaji = new CheckBox
            {
                Text = romaji.HasValue ? "Show romaji under Japanese lyrics" : "Romaji engine not found (RomajiCore.dll)",
                AutoSize = true, Anchor = AnchorStyles.Left, Checked = romaji ?? false, Enabled = romaji.HasValue
            };
            _chkRomaji.CheckedChanged += (s, e) => _setRomaji?.Invoke(_chkRomaji.Checked);
            table.Controls.Add(lblRomaji, 0, row + 6);
            table.Controls.Add(_chkRomaji, 1, row + 6);

            var lblHighlight = new Label { Text = "Word highlight:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = label1Margin() };
            var hlFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _chkWordHighlight = new CheckBox { Text = "Fill words as they are sung (word-timed .lrc)", AutoSize = true, Checked = _settings.WordHighlight, Anchor = AnchorStyles.Left };
            _btnHighlightColor = new Button { Width = btnColor1.Width / 2, Height = btnColor1.Height, BackColor = _settings.HighlightColor, FlatStyle = btnColor1.FlatStyle, Enabled = _settings.WordHighlight };
            _chkWordHighlight.CheckedChanged += (s, e) =>
            {
                _settings.WordHighlight = _chkWordHighlight.Checked;
                _btnHighlightColor.Enabled = _chkWordHighlight.Checked;
                SettingsChanged?.Invoke(this, _settings);
            };
            _btnHighlightColor.Click += (s, e) =>
            {
                _colorDialog.Color = _btnHighlightColor.BackColor;
                var res = _colorDialog.ShowDialog();
                if (res != DialogResult.OK && res != DialogResult.Yes) return;
                _btnHighlightColor.BackColor = _colorDialog.Color;
                _settings.HighlightColor = _colorDialog.Color;
                SettingsChanged?.Invoke(this, _settings);
            };
            hlFlow.Controls.Add(_chkWordHighlight);
            hlFlow.Controls.Add(_btnHighlightColor);
            _cmbLyricVersion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Anchor = AnchorStyles.Left };
            _cmbLyricVersion.Items.AddRange(new object[] { "Play the enhanced lyric", "Play the original lyric" });
            _cmbLyricVersion.SelectedIndex = _settings.PlayOriginalLyrics ? 1 : 0;
            _cmbLyricVersion.SelectedIndexChanged += (s, e) =>
            {
                _settings.PlayOriginalLyrics = _cmbLyricVersion.SelectedIndex == 1;
                SettingsChanged?.Invoke(this, _settings);
            };
            hlFlow.Controls.Add(_cmbLyricVersion);
            table.Controls.Add(lblHighlight, 0, row + 7);
            table.Controls.Add(hlFlow, 1, row + 7);

            var lblUpdates = new Label { Text = "Updates (v" + Updater.CurrentVersion + "):", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
            var updates = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            _chkCheckUpdates = new CheckBox { Text = "Check GitHub for new versions", AutoSize = true, Checked = _settings.CheckForUpdates };
            _chkAutoInstall = new CheckBox { Text = "Install them without asking (restart MusicBee to apply)", AutoSize = true, Checked = _settings.AutoInstallUpdates, Enabled = _settings.CheckForUpdates };
            var btnCheckNow = new Button { Text = "Check now", AutoSize = true };
            _chkCheckUpdates.CheckedChanged += (s, e) =>
            {
                _settings.CheckForUpdates = _chkCheckUpdates.Checked;
                _chkAutoInstall.Enabled = _chkCheckUpdates.Checked;
            };
            _chkAutoInstall.CheckedChanged += (s, e) => _settings.AutoInstallUpdates = _chkAutoInstall.Checked;
            btnCheckNow.Click += (s, e) => UpdateCheckRequested?.Invoke(this, EventArgs.Empty);
            updates.Controls.Add(_chkCheckUpdates);
            updates.Controls.Add(_chkAutoInstall);
            updates.Controls.Add(btnCheckNow);
            table.Controls.Add(lblUpdates, 0, row + 8);
            table.Controls.Add(updates, 1, row + 8);

            table.ResumeLayout(true);

            var preferred = table.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Math.Max(ClientSize.Width, preferred.Width), Math.Max(ClientSize.Height, preferred.Height));

            Padding label1Margin() => new Padding(3);
        }

        private void Settings_Load(object sender, EventArgs e)
        {
            _font = _settings.Font ?? DefaultFont;
            btnFont.Text = _font.Name + " "+ _font.Size;
            // Each value is applied on its own: one out-of-range setting must not stop the rest from
            // being restored, otherwise closing the dialog writes the designer defaults back to disk.
            SetSelectedIndex(comboBoxGradientType, _settings.GradientType);
            SetSelectedIndex(comboBoxAlignment, _settings.AlignmentType);
            if (comboBoxGradientType.SelectedIndex < 0) comboBoxGradientType.SelectedIndex = 0;
            if (comboBoxAlignment.SelectedIndex < 0) comboBoxAlignment.SelectedIndex = 0;
            btnColor1.BackColor = _settings.Color1;
            btnColor2.BackColor = _settings.Color2;
            btnBorderColor.BackColor = _settings.BorderColor;
            SetTrackBarValue(barBackgroundOpacity, _settings.BackgroundOpacity);
            checkBoxPreserveSlash.Checked = _settings.PreserveSlash;
            checkBoxAutoHide.Checked = _settings.AutoHide;
            checkBoxNextLineWhenNoTranslation.Checked = _settings.NextLineWhenNoTranslation;
            checkBoxHideWhenUnavailable.Checked = _settings.HideWhenUnavailable;
        }

        private static void SetSelectedIndex(ComboBox combo, int index)
        {
            if (index < 0 || index >= combo.Items.Count) return;
            combo.SelectedIndex = index;
        }

        private static void SetTrackBarValue(TrackBar bar, int value)
        {
            bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, value));
        }

        private void btnFont_Click(object sender, EventArgs e)
        {
            dlgFont.Font = _font;
            var res = dlgFont.ShowDialog();
            if (res != DialogResult.OK && res != DialogResult.Yes) return;
            var font = dlgFont.Font;
            // Force point unit
            _font = new Font(font.FontFamily, font.Size, font.Style, GraphicsUnit.Point, font.GdiCharSet);
            btnFont.Text = _font.Name + " " + _font.Size;
            _settings.Font = _font;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void btnColors_Click(object sender, EventArgs e)
        {
            var button = (Button) sender;
            _colorDialog.Color = button.BackColor;
            var res = _colorDialog.ShowDialog();
            if (res == DialogResult.OK || res == DialogResult.Yes)
                button.BackColor = _colorDialog.Color;
            _settings.Color1 = btnColor1.BackColor;
            _settings.Color2 = btnColor2.BackColor;
            _settings.BorderColor = btnBorderColor.BackColor;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void checkBoxPreserveSlash_CheckedChanged(object sender, EventArgs e)
        {
            _settings.PreserveSlash = checkBoxPreserveSlash.Checked;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void checkBoxAutoHide_CheckedChanged(object sender, EventArgs e)
        {
            _settings.AutoHide = checkBoxAutoHide.Checked;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void checkBoxNextLineWhenNoTranslation_CheckedChanged(object sender, EventArgs e)
        {
            _settings.NextLineWhenNoTranslation = checkBoxNextLineWhenNoTranslation.Checked;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void barBackgroundOpacity_Scroll(object sender, EventArgs e)
        {
            _settings.BackgroundOpacity = barBackgroundOpacity.Value;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void comboBoxGradientType_SelectedIndexChanged(object sender, EventArgs e)
        {
            _settings.GradientType = comboBoxGradientType.SelectedIndex;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void comboBoxAlignment_SelectedIndexChanged(object sender, EventArgs e)
        {
            _settings.AlignmentType = comboBoxAlignment.SelectedIndex;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void checkBoxHideWhenUnavailable_CheckedChanged(object sender, EventArgs e)
        {
            _settings.HideWhenUnavailable = checkBoxHideWhenUnavailable.Checked;
            SettingsChanged?.Invoke(this, _settings);
        }

        private void Settings_FormClosed(object sender, FormClosedEventArgs e)
        {
            _settings.Font = _font;
            _settings.Color1 = btnColor1.BackColor;
            _settings.Color2 = btnColor2.BackColor;
            _settings.BorderColor = btnBorderColor.BackColor;
            _settings.GradientType = comboBoxGradientType.SelectedIndex;
            _settings.AlignmentType = comboBoxAlignment.SelectedIndex;
            _settings.BackgroundOpacity = barBackgroundOpacity.Value;
            _settings.PreserveSlash = checkBoxPreserveSlash.Checked;
            _settings.AutoHide = checkBoxAutoHide.Checked;
            _settings.HideWhenUnavailable = checkBoxHideWhenUnavailable.Checked;
            _settings.TextOpacity = _barTextOpacity.Value;
            _settings.UseLine2Color = _chkLine2Color.Checked;
            _settings.Line2Color = _btnLine2Color.BackColor;
            _settings.BoxBackground = _chkBox.Checked;
            _settings.CheckForUpdates = _chkCheckUpdates.Checked;
            _settings.AutoInstallUpdates = _chkAutoInstall.Checked;
            _settings.WordHighlight = _chkWordHighlight.Checked;
            _settings.HighlightColor = _btnHighlightColor.BackColor;
            _settings.FixedBoxPerSong = _chkFixedBox.Checked;
            _settings.ContinuousMode = _chkContinuous.Checked;
            _settings.PlayOriginalLyrics = _cmbLyricVersion.SelectedIndex == 1;
        }
    }

    public class SettingsObj
    {
        public Color Color1;
        public Color Color2;
        public Color BorderColor;
        public int GradientType;
        public int AlignmentType;
        public int BackgroundOpacity;
        public bool PreserveSlash;
        public bool AutoHide;
        public bool NextLineWhenNoTranslation;
        public bool HideOnStartup;
        public bool HideWhenUnavailable;
        public int TextOpacity = 100;           // percent, applied to the whole lyrics window
        public bool UseLine2Color;
        public Color Line2Color = Color.FromArgb(170, 210, 255);
        public bool BoxBackground;              // one padded box behind all lines
        public bool SmoothScroll = true;        // lines slide up into place instead of cutting
        public int SmoothScrollDuration = 500;  // ms
        public int SmoothScrollFps = 60;        // animation frame rate
        public bool CheckForUpdates = true;     // look for new GitHub releases at startup and every 12 h
        public bool AutoInstallUpdates;         // install them without asking first
        public bool WordHighlight = true;       // fill words as they are sung (enhanced LRC with word timing)
        public Color HighlightColor = Color.FromArgb(0x50, 0xE6, 0xF0);
        public int KaraokeX = -1, KaraokeY = -1, KaraokeW = -1, KaraokeH = -1;   // karaoke window bounds
        public bool FixedBoxPerSong;            // single box sized to the song's largest line, for the whole song
        public bool ShowMoveTab;                // small handle above the lyrics for dragging them
        public bool ContinuousMode;             // current + next line on screen, rolling up like a strip of paper
        public bool PlayOriginalLyrics;         // "<song>.original.lrc" (official lyric) instead of the enhanced .lrc
        public bool KaraokeFullscreen;
        public int PanelX = -1, PanelY = -1, PanelW = -1, PanelH = -1;   // lyrics panel (BETA) bounds
        public int PanelOpacity = 90;           // percent
        public float PanelFontScale = 1f;
        public bool PanelOpen;                  // reopen the panel when MusicBee starts
        public string SkippedVersion;           // release tag the user chose to skip
        public int PosY = -1;
        public int PosX = -1;
        [JsonConverter(typeof(FontConverter))]
        public Font Font;

        public static SettingsObj GenerateDefault()
        {
            var font = new Font(FontFamily.GenericSansSerif, 34.0f, FontStyle.Regular, GraphicsUnit.Point);
            return new SettingsObj
            {
                BorderColor = Color.Black,
                Color1 = Color.GhostWhite,
                Color2 = Color.LightGray,
                Font = font,
                GradientType = 1,
                AlignmentType = 0,
                BackgroundOpacity = 40,
            };
        }
    }

    // This corresponds to items of comboBoxGradientType!
    public enum GradientType
    {
        NoGradient = 0,
        DoubleColor = 1,
        TripleColor = 2
    }

    // This corresponds to items of comboBoxAlignment!
    public enum AlignmentType
    {
        Center = 0,
        Left = 1,
        Right = 2
    }
}
