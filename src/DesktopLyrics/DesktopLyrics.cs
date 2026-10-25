// Modified by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): Locates the bundled romaji dictionary; opens the settings dialog from the lyrics right-click menu and saves changes made there;
// romaji on/off switch; update check against this fork's GitHub releases.
// Original work: Copyright Charlie Jiang, licensed under the Apache License 2.0.
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Timers;
using System.Windows.Forms;
using Newtonsoft.Json;
using Exception = System.Exception;
using Timer = System.Timers.Timer;

namespace MusicBeePlugin
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    // ReSharper disable once ClassNeverInstantiated.Global
    public partial class Plugin
    {
        private const long UpdateIntervalMs = 100L;
        private const string SettingsFileName = "desktopLyrics.json";
        private const string SettingsFileName2 = "desktopLyrics_Window.set";

        // MB related
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();
        private string SettingsPath => Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), SettingsFileName);
        private string SettingsPath2 => Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), SettingsFileName2);
        // Customed
        private volatile SettingsObj _settings;
        private volatile FrmLyrics _frmLyrics;
        private volatile FrmKaraoke _frmKaraoke;
        private Timer _timer;
        private LyricsController _lyricsCtrl;
        private readonly object _lock = new object();

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            var versions = Assembly.GetExecutingAssembly().GetName().Version.ToString().Split('.');

            _mbApiInterface = new MusicBeeApiInterface();
            _mbApiInterface.Initialise(apiInterfacePtr);
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "Desktop Lyrics";
            _about.Description = "Display lyrics on your desktop!";
            _about.Author = "Charlie Jiang (romaji fork: nishizumi-maho)";
            _about.TargetApplication = "";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.General;
            _about.VersionMajor = short.Parse(versions[0]);  // your plugin version
            _about.VersionMinor = short.Parse(versions[1]);
            _about.Revision = short.Parse(versions[2]);
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            _about.ConfigurationPanelHeight = 0;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            // Must run before anything loads RomajiCore or the dictionary: swaps in files a previous
            // update could not replace while MusicBee was running.
            try { Updater.FinishPendingUpdate(); }
            catch (Exception e) { _mbApiInterface.MB_Trace("Desktop Lyrics: finishing update failed: " + e); }

            _lyricsCtrl = new LyricsController(_mbApiInterface);
            try { SetRomajiDictionary(); }
            catch (Exception e) when (e is FileNotFoundException || e is FileLoadException || e is TypeLoadException)
            {
                LyricsController.RomajiAvailable = false; // RomajiCore.dll missing from the Plugins folder
            }
            return _about;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void SetRomajiDictionary()
        {
            RomajiCore.Romanizer.UseFirstExistingDictionary(
                Path.Combine(Path.GetDirectoryName(typeof(RomajiCore.Romanizer).Assembly.Location) ?? "", "RomajiLyrics", "IpaDic"),
                Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), "Plugins", "RomajiLyrics", "IpaDic"),
                Path.Combine(Application.StartupPath, "Plugins", "RomajiLyrics", "IpaDic"));
        }

        // ReSharper disable once UnusedParameter.Global
        public bool Configure(IntPtr panelHandle)
        {
            if (_settings == null) _settings = SettingsObj.GenerateDefault();
            var settingsForm = new FrmSettings(_settings, _lyricsCtrl.GetRomajiSetting, _lyricsCtrl.SetRomajiSetting);
            settingsForm.SettingsChanged += (sender, settings) =>
            {
                _frmLyrics?.UpdateFromSettings(settings);
                _frmKaraoke?.UpdateFromSettings(settings);
            };
            settingsForm.UpdateCheckRequested += (sender, args) => CheckForUpdatesAsync(manual: true);
            settingsForm.ShowDialog();
            ApplyUpdateSchedule();
            SaveSettings(_settings);
            LyricParser.PreserveSlash = _settings.PreserveSlash;
            _lyricsCtrl.NextLineWhenNoTranslation = _settings.NextLineWhenNoTranslation;
            _lyricsCtrl.LeadInMs = LeadIn(_settings);
            var frm = _frmLyrics;
            frm?.Invoke(new Action(() =>
            {
                frm?.UpdateFromSettings(_settings);
            }));
            return true;
        }

        private void SaveSettings(SettingsObj settings)
        {
            if (settings == null) return;
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings));
        }

        public void SaveSettings()
        {
            // Still, we've saved all settings already.
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        // ReSharper disable once UnusedParameter.Global
        public void Close(PluginCloseReason reason)
        {
            Updater.Stop();
            _timer?.Stop();
            var frm = _frmLyrics;
            frm?.Invoke(new Action(() =>
            {
                frm?.Hide();
                _frmLyrics = null;
            }));
            // Closing the karaoke window stores its bounds in the settings, so do it before saving them.
            var karaoke = _frmKaraoke;
            if (karaoke != null && !karaoke.IsDisposed)
            {
                try { karaoke.Invoke(new Action(karaoke.Close)); }
                catch (InvalidOperationException) { }
            }
            SaveSettings(_settings);
        }
        
        public void Uninstall()
        {
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            if (File.Exists(SettingsPath2)) File.Delete(SettingsPath2); 
        }

        // ReSharper disable once UnusedParameter.Global
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // perform some action depending on the notification type
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (type)
            {
                case NotificationType.PluginStartup:
                    // while (!Debugger.IsAttached) System.Threading.Thread.Sleep(1);
                    try
                    {
                        try
                        {
                            _settings = JsonConvert.DeserializeObject<SettingsObj>(File.ReadAllText(SettingsPath))
                                        ?? SettingsObj.GenerateDefault();
                        }
                        catch (Exception)
                        {
                            _settings = SettingsObj.GenerateDefault();
                        }

                        Application.EnableVisualStyles();
                        LyricParser.PreserveSlash = _settings.PreserveSlash;
                        _lyricsCtrl.NextLineWhenNoTranslation = _settings.NextLineWhenNoTranslation;
                        _lyricsCtrl.LeadInMs = LeadIn(_settings);
                        try
                        {
                            // Throws once any window already exists in the host process; that must not
                            // abort startup, or the whole plugin silently never opens.
                            Application.SetCompatibleTextRenderingDefault(false);
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        StartupMenuItem();
                        if (!_settings.HideOnStartup)
                            StartupForm();

                        if (_timer != null && _timer.Enabled) _timer.Enabled = false;
                        _timer = new Timer(UpdateIntervalMs) {AutoReset = false};
                        _timer.Elapsed += TimerTick;
                        _timer.Start();

                        ApplyUpdateSchedule();
                    }
                    catch (Exception e)
                    {
                        _mbApiInterface.MB_Trace(e.ToString());
                    }
                    break;
                case NotificationType.PlayStateChanged:
                    UpdatePlayState(_mbApiInterface.Player_GetPlayState());
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    try
                    {
                        UpdateLyrics(force: true);
                    }
                    catch (Exception e)
                    {
                        _mbApiInterface.MB_Trace(e.ToString());
                    }
                    break;
                case NotificationType.TagsChanged:
                    try
                    {
                        UpdateLyrics(force: true);
                    }
                    catch (Exception e)
                    {
                        _mbApiInterface.MB_Trace(e.ToString());
                    }
                    break;
            }
        }

        // Change form according to play state
        private void UpdatePlayState(PlayState state)
        {
            if (_frmLyrics == null) return;
            switch (state)
            {
                case PlayState.Stopped:
                    _frmLyrics.SetPlayStateVisible(false);
                    break;
                case PlayState.Playing:
                    _frmLyrics.SetPlayStateVisible(true);
                    break;
            }
        }

        private void StartupMenuItem()
        {
            // MenuItem will only be returned before we construct the handler lambda expression, so we have to use a "slot"
            // (which will be filled after MB_AddMenuItem returns) to hold the menuItem to be referred in the closure.
            var menuItemSlot = new ToolStripMenuItem[] { null };

            var menuItem = (ToolStripMenuItem) _mbApiInterface.MB_AddMenuItem(
                "mnuView/Desktop Lyrics", "Toggle Desktop Lyrics visibility.",
                ToggleLyrics);
            menuItemSlot[0] = menuItem;
            menuItem.Checked = !_settings.HideOnStartup;
            _mbApiInterface.MB_AddMenuItem("mnuView/Desktop Lyrics Karaoke", "Open the karaoke lyrics window.",
                                           (s, a) => OpenKaraoke());
            return;

            void ToggleLyrics(object sender, EventArgs args)
            {
                var menuItem2 = menuItemSlot[0];
                if (menuItem2 == null) return; // BUG: multithread race condition

                menuItem2.Checked = !menuItem2.Checked;
                if (!menuItem2.Checked)
                {
                    // Drop the reference too: the lyrics timer would otherwise keep invoking a disposed form.
                    lock (_lock)
                    {
                        _frmLyrics?.Dispose();
                        _frmLyrics = null;
                        _line1 = _line2 = null;
                    }
                }
                else
                    StartupForm();
                _settings.HideOnStartup = !menuItem2.Checked;
                SaveSettings(_settings);
            }
        }

        private void StartupForm()
        {
            _frmLyrics?.Dispose();
            _frmLyrics = null;
            // MusicBee hands us a raw HWND; it is not guaranteed to belong to a WinForms Form.
            if (Control.FromHandle(_mbApiInterface.MB_GetWindowHandle()) is Form owner)
                owner.Invoke(new Action(CreateLyricsForm));
            else
                CreateLyricsForm();
        }

        private void CreateLyricsForm()
        {
            _frmLyrics = new FrmLyrics(_settings);
            _frmLyrics.SettingsRequested += (s, e) => Configure(IntPtr.Zero);
            _frmLyrics.SettingsEdited += (s, e) => SaveSettings(_settings);
            _frmLyrics.RomajiStateProvider = _lyricsCtrl.GetRomajiSetting;
            _frmLyrics.RomajiToggled += (s, on) => _lyricsCtrl.SetRomajiSetting(on);
            _frmLyrics.UpdateCheckRequested += (s, e) => CheckForUpdatesAsync(manual: true);
            _frmLyrics.KaraokeRequested += (s, e) => OpenKaraoke();
            _frmLyrics.Show();
            try
            {
                UpdateLyrics(force: true);
            }
            catch (Exception e)
            {
                _mbApiInterface.MB_Trace(e.ToString());
            }
        }
        /// <summary>Opens the karaoke window, or brings it to the front if it is already open. Created on
        /// MusicBee's UI thread, like the lyrics window.</summary>
        private void OpenKaraoke()
        {
            void Create()
            {
                var existing = _frmKaraoke;
                if (existing != null && !existing.IsDisposed)
                {
                    if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
                    existing.Activate();
                    return;
                }
                var form = new FrmKaraoke(_settings);
                form.FormClosed += (s, e) =>
                {
                    _frmKaraoke = null;
                    SaveSettings(_settings);   // FormClosing stored the window bounds in the settings
                };
                _frmKaraoke = form;
                form.Show();
                form.SetLyrics(_lyricsCtrl.CurrentLyrics);
                form.SyncPlayback(_mbApiInterface.Player_GetPosition(),
                                  _mbApiInterface.Player_GetPlayState() == PlayState.Playing);
            }

            try
            {
                if (Control.FromHandle(_mbApiInterface.MB_GetWindowHandle()) is Form owner && owner.InvokeRequired)
                    owner.Invoke(new Action(Create));
                else
                    Create();
            }
            catch (Exception e)
            {
                _mbApiInterface.MB_Trace(e.ToString());
            }
        }

        private void TimerTick(object sender, ElapsedEventArgs args)
        {
            Debug.Assert(sender is Timer);
            try
            {
                UpdateLyrics();
            }
            catch (Exception e)
            {
                _mbApiInterface.MB_Trace(e.ToString());
            }
            ((Timer) sender).Start();
        }

        // Word-timed lines arrive this early so the slide-in has finished when the first word is sung;
        // the extra 100 ms covers the position poll interval.
        private static double LeadIn(SettingsObj s) =>
            s.SmoothScroll ? Math.Max(200, Math.Min(1500, s.SmoothScrollDuration)) + UpdateIntervalMs : UpdateIntervalMs;

        private string _line1, _line2;
        private void UpdateLyrics(bool force = false)
        {
            lock (_lock)
            {
                if (_frmLyrics == null && _frmKaraoke == null) return;
                var entry = _lyricsCtrl.UpdateLyrics(!_settings.HideWhenUnavailable);
                var position = _mbApiInterface.Player_GetPosition();
                var playing = _mbApiInterface.Player_GetPlayState() == PlayState.Playing;
                // Both windows marshal these calls to their own thread.
                var karaoke = _frmKaraoke;
                if (karaoke != null)
                {
                    karaoke.SetLyrics(_lyricsCtrl.CurrentLyrics);
                    karaoke.SyncPlayback(position, playing);
                }
                if (_frmLyrics == null) return;
                _frmLyrics.SyncPlayback(position, playing);
                if (entry == null && _line1 != "")
                {
                    _line1 = "";
                    _frmLyrics.Clear();
                    return;
                }

                if (entry == null) return;
                if (!force && entry.LyricLine1 == _line1 && entry.LyricLine2 == _line2) return;
                _frmLyrics.UpdateLyrics(entry);   // marshals to the form's thread
                _line1 = entry.LyricLine1;
                _line2 = entry.LyricLine2;
            }
        }

        public string[] GetProviders() { return null; }

        // ---- updates ----

        private Version _announcedVersion;   // "Later" was picked for this one; don't ask again this session

        private void ApplyUpdateSchedule()
        {
            if (_settings != null && _settings.CheckForUpdates)
                Updater.Schedule(() => CheckForUpdates(manual: false));
            else
                Updater.Stop();
        }

        private void CheckForUpdatesAsync(bool manual)
        {
            System.Threading.Tasks.Task.Run(() => CheckForUpdates(manual));
        }

        /// <summary>Runs on a worker thread. A manual check always reports back; a scheduled one stays
        /// silent unless there is something new the user has not skipped or postponed.</summary>
        private void CheckForUpdates(bool manual)
        {
            if (!Updater.TryBeginCheck()) return;
            try
            {
                var release = Updater.GetLatestRelease();
                if (release == null || release.Version <= Updater.CurrentVersion)
                {
                    if (manual) ShowMessage($"You have the latest version of Desktop Lyrics ({Updater.CurrentVersion}).");
                    return;
                }
                if (!manual && (release.Tag == _settings.SkippedVersion || release.Version.Equals(_announcedVersion))) return;
                _announcedVersion = release.Version;

                var installed = false;
                if (!manual && _settings.AutoInstallUpdates)
                {
                    try
                    {
                        Updater.Install(release);
                        installed = true;
                    }
                    catch (Exception e)
                    {
                        // Fall back to asking, so the user still hears about the release.
                        _mbApiInterface.MB_Trace("Desktop Lyrics: automatic update failed: " + e);
                    }
                }

                RunOnUi((owner, modal) =>
                {
                    var dialog = new FrmUpdate(release, installed);
                    dialog.SkipRequested += (s, e) =>
                    {
                        _settings.SkippedVersion = release.Tag;
                        SaveSettings(_settings);
                    };
                    if (modal)
                    {
                        dialog.ShowDialog();
                        dialog.Dispose();
                        return;
                    }
                    dialog.FormClosed += (s, e) => dialog.Dispose();
                    dialog.Show(owner);
                });
            }
            catch (Exception e)
            {
                _mbApiInterface.MB_Trace("Desktop Lyrics: update check failed: " + e);
                if (manual) ShowMessage("Could not check for updates: " + e.GetBaseException().Message);
            }
            finally
            {
                Updater.EndCheck();
            }
        }

        private void ShowMessage(string text)
        {
            RunOnUi((owner, modal) => MessageBox.Show(owner, text, "Desktop Lyrics", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }

        /// <summary>Runs <paramref name="action"/> on a UI thread: MusicBee's (with its main window as owner)
        /// or the lyrics window's. With neither available it gets an STA thread of its own and must show
        /// its window modally (second argument true) to keep that thread pumping messages.</summary>
        private void RunOnUi(Action<IWin32Window, bool> action)
        {
            if (Control.FromHandle(_mbApiInterface.MB_GetWindowHandle()) is Form main && main.IsHandleCreated)
            {
                main.BeginInvoke(new Action(() => action(main, false)));
                return;
            }
            var lyrics = _frmLyrics;
            if (lyrics != null && lyrics.IsHandleCreated)
            {
                lyrics.BeginInvoke(new Action(() => action(null, false)));
                return;
            }
            var thread = new System.Threading.Thread(() => action(null, true));
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
   }
}