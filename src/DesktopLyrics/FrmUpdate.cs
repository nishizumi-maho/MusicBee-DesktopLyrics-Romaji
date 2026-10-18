// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): dialog that announces and installs a new release.
// Licensed under the Apache License 2.0, like the rest of the project.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public class FrmUpdate : Form
    {
        private readonly Updater.ReleaseInfo _release;
        private readonly Label _status;
        private readonly Button _btnUpdate, _btnPage, _btnSkip, _btnLater;

        /// <summary>Raised when the user picks "Skip this version".</summary>
        public event EventHandler SkipRequested;

        /// <param name="alreadyInstalled">True when automatic installation already put the files in place,
        /// so the dialog only has to tell the user to restart.</param>
        public FrmUpdate(Updater.ReleaseInfo release, bool alreadyInstalled)
        {
            _release = release;

            Text = "Desktop Lyrics update";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(520, 380);
            Padding = new Padding(12);
            Font = SystemFonts.MessageBoxFont;

            var heading = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 44,
                Text = $"Desktop Lyrics (romaji fork) {release.Version} is available.\r\nYou have version {Updater.CurrentVersion}."
            };
            var notes = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = string.IsNullOrWhiteSpace(release.Notes) ? "(no release notes)" : release.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                BackColor = SystemColors.Window
            };
            _status = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 40, TextAlign = ContentAlignment.MiddleLeft };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                WrapContents = false, Padding = new Padding(0, 6, 0, 0)
            };
            _btnLater = new Button { Text = "Later", AutoSize = true, DialogResult = DialogResult.Cancel };
            _btnSkip = new Button { Text = "Skip this version", AutoSize = true };
            _btnPage = new Button { Text = "Release page", AutoSize = true };
            _btnUpdate = new Button { Text = "Update now", AutoSize = true };
            buttons.Controls.AddRange(new Control[] { _btnLater, _btnSkip, _btnPage, _btnUpdate });

            Controls.Add(notes);
            Controls.Add(heading);
            Controls.Add(_status);
            Controls.Add(buttons);
            AcceptButton = _btnUpdate;
            ActiveControl = _btnUpdate;   // otherwise the notes box takes focus and shows all its text selected
            CancelButton = _btnLater;

            _btnPage.Click += (s, e) => OpenReleasePage();
            _btnSkip.Click += (s, e) =>
            {
                SkipRequested?.Invoke(this, EventArgs.Empty);
                Close();
            };
            _btnUpdate.Click += (s, e) => StartInstall();

            if (alreadyInstalled) ShowInstalled();
        }

        private void OpenReleasePage()
        {
            try { Process.Start(new ProcessStartInfo(_release.PageUrl ?? Updater.ReleasesPage) { UseShellExecute = true }); }
            catch (Exception) { /* no browser registered; nothing useful to do */ }
        }

        private void StartInstall()
        {
            _btnUpdate.Enabled = _btnSkip.Enabled = _btnLater.Enabled = false;
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = "Downloading and installing...";
            UseWaitCursor = true;

            Task.Run(() => Updater.Install(_release)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    UseWaitCursor = false;
                    if (t.Exception == null)
                    {
                        ShowInstalled();
                        return;
                    }
                    var error = t.Exception.GetBaseException();
                    _status.ForeColor = Color.Firebrick;
                    _status.Text = (error is UnauthorizedAccessException
                        ? "Windows did not allow writing to the Plugins folder. "
                        : "The update failed: " + error.Message + " ") + "Nothing was changed; you can install it by hand from the release page.";
                    _btnUpdate.Enabled = _btnSkip.Enabled = _btnLater.Enabled = true;
                }));
            });
        }

        private void ShowInstalled()
        {
            Text = "Desktop Lyrics updated";
            _status.ForeColor = Color.DarkGreen;
            _status.Text = $"Version {_release.Version} is installed. Restart MusicBee to start using it.";
            _btnUpdate.Visible = false;
            _btnSkip.Visible = false;
            _btnLater.Text = "OK";
            _btnLater.Enabled = true;
            AcceptButton = _btnLater;
        }
    }
}
