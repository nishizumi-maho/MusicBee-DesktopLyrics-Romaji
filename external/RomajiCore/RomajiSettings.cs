using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RomajiCore
{
    /// <summary>
    /// Settings shared by the Romaji Lyrics panel and Desktop Lyrics. Stored in MusicBee's
    /// persistent storage folder, so every Windows install that shares that folder shares them.
    /// </summary>
    public class RomajiSettings
    {
        public const string FileName = "RomajiLyrics.settings";

        public bool RomajiEnabled = true;
        public bool ShowOriginal = true;
        public bool ShowTranslation = true;
        public float FontSize = 13f;
        public bool ScaleWithWindow = true;
        public bool WindowOpen = true;
        public int WindowX = -1, WindowY = -1, WindowW = 460, WindowH = 620;

        public static event EventHandler Changed;

        private static readonly object Sync = new object();
        private static RomajiSettings _current;
        private static string _path;
        private static DateTime _loadedStamp, _lastCheck;

        /// <summary>Loads (or re-loads after another plugin saved) the shared settings.</summary>
        public static RomajiSettings Get(string storageFolder)
        {
            lock (Sync)
            {
                var path = Path.Combine(storageFolder, FileName);
                // Plugins in this process share the instance; only look at the disk every few seconds.
                if (_current != null && path == _path && (DateTime.UtcNow - _lastCheck).TotalSeconds < 3) return _current;
                _lastCheck = DateTime.UtcNow;
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_current != null && path == _path && stamp == _loadedStamp) return _current;
                _path = path;
                _loadedStamp = stamp;
                _current = Read(path);
                return _current;
            }
        }

        public void Save()
        {
            lock (Sync)
            {
                if (_path == null) return;
                var sb = new StringBuilder();
                var ci = CultureInfo.InvariantCulture;
                sb.AppendLine("RomajiEnabled=" + RomajiEnabled);
                sb.AppendLine("ShowOriginal=" + ShowOriginal);
                sb.AppendLine("ShowTranslation=" + ShowTranslation);
                sb.AppendLine("FontSize=" + FontSize.ToString(ci));
                sb.AppendLine("ScaleWithWindow=" + ScaleWithWindow);
                sb.AppendLine("WindowOpen=" + WindowOpen);
                sb.AppendLine(string.Format(ci, "Window={0},{1},{2},{3}", WindowX, WindowY, WindowW, WindowH));
                try
                {
                    File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
                    _loadedStamp = File.GetLastWriteTimeUtc(_path);
                }
                catch (IOException) { /* read-only or locked: keep in memory */ }
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static RomajiSettings Read(string path)
        {
            var s = new RomajiSettings();
            if (!File.Exists(path)) return s;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var i = line.IndexOf('=');
                    if (i > 0) values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch (IOException) { return s; }

            var ci = CultureInfo.InvariantCulture;
            if (values.TryGetValue("RomajiEnabled", out var v) && bool.TryParse(v, out var b)) s.RomajiEnabled = b;
            if (values.TryGetValue("ShowOriginal", out v) && bool.TryParse(v, out b)) s.ShowOriginal = b;
            if (values.TryGetValue("ShowTranslation", out v) && bool.TryParse(v, out b)) s.ShowTranslation = b;
            if (values.TryGetValue("FontSize", out v) && float.TryParse(v, NumberStyles.Float, ci, out var f) && f >= 6 && f <= 60) s.FontSize = f;
            if (values.TryGetValue("ScaleWithWindow", out v) && bool.TryParse(v, out b)) s.ScaleWithWindow = b;
            if (values.TryGetValue("WindowOpen", out v) && bool.TryParse(v, out b)) s.WindowOpen = b;
            if (values.TryGetValue("Window", out v))
            {
                var p = v.Split(',');
                if (p.Length == 4 && int.TryParse(p[0], out var x) && int.TryParse(p[1], out var y) &&
                    int.TryParse(p[2], out var w) && int.TryParse(p[3], out var h))
                {
                    s.WindowX = x; s.WindowY = y; s.WindowW = Math.Max(200, w); s.WindowH = Math.Max(150, h);
                }
            }
            return s;
        }
    }
}
