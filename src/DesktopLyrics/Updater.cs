// Added by nishizumi-maho, 2026 (Desktop Lyrics romaji fork): update check against the GitHub releases of this fork.
// Licensed under the Apache License 2.0, like the rest of the project.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin
{
    /// <summary>
    /// Looks for a newer release of this plugin on GitHub and installs it into the Plugins folder.
    ///
    /// Only two kinds of request are ever made: one to the GitHub API for the latest release, and,
    /// when the user agrees to update, the download of that release's zip and its .sha256 file. Nothing
    /// about the user or their library is sent.
    ///
    /// Files that MusicBee has loaded (the plugin DLL itself, RomajiCore, the dictionary) cannot be
    /// overwritten while it runs, but Windows does allow renaming them. They are moved aside as
    /// *.updatebak and the new file takes the original name; MusicBee picks the new files up on its next
    /// start, and the leftovers are deleted then. A file that cannot even be renamed is written as
    /// *.updatepending and swapped in on the next start, before anything loads it.
    /// </summary>
    public static class Updater
    {
        public const string Repository = "nishizumi-maho/MusicBee-DesktopLyrics-Romaji";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
        private const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
        private const string AssetPrefix = "MusicBee-DesktopLyrics-Romaji-";
        private const string BackupSuffix = ".updatebak";
        private const string PendingSuffix = ".updatepending";

        // Shared with other plugins. Never replace one of these with an older copy than the one installed.
        private static readonly HashSet<string> SharedLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Newtonsoft.Json.dll", "Cyotek.Windows.Forms.ColorPicker.dll", "LibNMeCab.dll", "RomajiCore.dll"
        };

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string PluginDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public class ReleaseInfo
        {
            public Version Version;
            public string Tag;
            public string Name;
            public string Notes;
            public string PageUrl;
            public string ZipUrl;
            public string Sha256Url;
        }

        /// <summary>Asks GitHub for the latest published (non-draft, non-prerelease) release.</summary>
        public static ReleaseInfo GetLatestRelease()
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(Download(LatestReleaseApi, "application/vnd.github+json")));
            var tag = (string)json["tag_name"] ?? "";
            if (!TryParseVersion(tag, out var version)) return null;

            var info = new ReleaseInfo
            {
                Version = version,
                Tag = tag,
                Name = (string)json["name"] ?? tag,
                Notes = (string)json["body"] ?? "",
                PageUrl = (string)json["html_url"] ?? ReleasesPage,
            };
            if (json["assets"] is JArray assets)
            {
                foreach (var asset in assets)
                {
                    var name = (string)asset["name"] ?? "";
                    var url = (string)asset["browser_download_url"];
                    if (!name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) info.ZipUrl = url;
                    else if (name.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase)) info.Sha256Url = url;
                }
            }
            return info;
        }

        public static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            var s = (tag ?? "").Trim().TrimStart('v', 'V');
            var dash = s.IndexOf('-');
            if (dash >= 0) s = s.Substring(0, dash);
            if (!Version.TryParse(s, out var v)) return false;
            version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            return true;
        }

        /// <summary>
        /// Downloads the release, checks it against its published SHA-256 and copies it into the
        /// Plugins folder. Throws with a readable message when anything is wrong; on failure every file
        /// already replaced is put back.
        /// </summary>
        public static void Install(ReleaseInfo release)
        {
            if (release.ZipUrl == null || release.Sha256Url == null)
                throw new InvalidOperationException("The release has no plugin package attached.");

            var zipBytes = Download(release.ZipUrl, "application/octet-stream");
            var expected = Encoding.ASCII.GetString(Download(release.Sha256Url, "application/octet-stream"))
                .Trim().Split(' ', '\t')[0].ToLowerInvariant();
            string actual;
            using (var sha = SHA256.Create())
                actual = string.Concat(sha.ComputeHash(zipBytes).Select(b => b.ToString("x2")));
            if (expected.Length != 64 || actual != expected)
                throw new InvalidOperationException("The downloaded package does not match its published SHA-256 checksum, so it was not installed.");

            var staging = Path.Combine(Path.GetTempPath(), "DesktopLyricsUpdate", release.Version.ToString());
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            try
            {
                Extract(zipBytes, staging);

                var newPlugin = Path.Combine(staging, "mb_DesktopLyrics.dll");
                if (!File.Exists(newPlugin))
                    throw new InvalidOperationException("The package does not contain mb_DesktopLyrics.dll.");
                var packaged = AssemblyName.GetAssemblyName(newPlugin).Version;
                if (packaged.Major != release.Version.Major || packaged.Minor != release.Version.Minor || packaged.Build != release.Version.Build)
                    throw new InvalidOperationException($"The package contains version {packaged}, not {release.Version}.");

                CopyIntoPlugins(staging, PluginDirectory);
            }
            finally
            {
                try { Directory.Delete(staging, true); } catch (Exception) { /* temp folder, best effort */ }
            }
        }

        private static void Extract(byte[] zipBytes, string target)
        {
            var root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
                    // Refuse entries that would land outside the staging folder ("zip slip").
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The package contains an invalid path: " + entry.FullName);
                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            }
        }

        private static void CopyIntoPlugins(string staging, string plugins)
        {
            var done = new List<(string dest, string backup)>();   // backup == null: the file is new
            try
            {
                foreach (var source in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                {
                    var relative = source.Substring(staging.Length).TrimStart(Path.DirectorySeparatorChar);
                    var dest = Path.Combine(plugins, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));

                    if (File.Exists(dest))
                    {
                        if (SameContent(source, dest)) continue;
                        if (SharedLibraries.Contains(Path.GetFileName(dest)) && !IsNewer(source, dest)) continue;

                        var backup = dest + BackupSuffix;
                        if (File.Exists(backup))
                        {
                            // A leftover from an earlier update; still loaded if that update ran in this session.
                            try { File.Delete(backup); }
                            catch (Exception) { backup = dest + "." + DateTime.UtcNow.Ticks + BackupSuffix; }
                        }
                        try
                        {
                            File.Move(dest, backup);
                        }
                        catch (IOException)
                        {
                            // Even a rename is refused: stage it for the next start instead.
                            File.Copy(source, dest + PendingSuffix, true);
                            done.Add((dest + PendingSuffix, null));
                            continue;
                        }
                        done.Add((dest, backup));
                        File.Copy(source, dest);
                    }
                    else
                    {
                        File.Copy(source, dest);
                        done.Add((dest, null));
                    }
                }
            }
            catch (Exception)
            {
                for (var i = done.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (File.Exists(done[i].dest)) File.Delete(done[i].dest);
                        if (done[i].backup != null) File.Move(done[i].backup, done[i].dest);
                    }
                    catch (Exception) { /* best effort; the original exception is what matters */ }
                }
                throw;
            }
        }

        /// <summary>
        /// Run once at startup, before RomajiCore or the dictionary are loaded: swaps in files that could not
        /// be replaced last time and removes the backups of files that were.
        /// </summary>
        public static void FinishPendingUpdate()
        {
            var plugins = PluginDirectory;
            foreach (var dir in new[] { plugins, Path.Combine(plugins, "DesktopLyrics"), Path.Combine(plugins, "RomajiLyrics") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var pending in Directory.GetFiles(dir, "*" + PendingSuffix, SearchOption.AllDirectories))
                {
                    var dest = pending.Substring(0, pending.Length - PendingSuffix.Length);
                    try
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(pending, dest);
                    }
                    catch (Exception) { /* still in use; try again next start */ }
                }
                foreach (var backup in Directory.GetFiles(dir, "*" + BackupSuffix, SearchOption.AllDirectories))
                {
                    try { File.Delete(backup); }
                    catch (Exception) { /* still loaded by this process; next start */ }
                }
            }
        }

        private static bool SameContent(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            using (var sha = SHA256.Create())
            {
                byte[] ha, hb;
                using (var s = File.OpenRead(a)) ha = sha.ComputeHash(s);
                using (var s = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) hb = sha.ComputeHash(s);
                return ha.SequenceEqual(hb);
            }
        }

        private static bool IsNewer(string source, string dest)
        {
            try
            {
                var s = FileVersionInfo.GetVersionInfo(source);
                var d = FileVersionInfo.GetVersionInfo(dest);
                var sv = new Version(s.FileMajorPart, s.FileMinorPart, s.FileBuildPart, s.FilePrivatePart);
                var dv = new Version(d.FileMajorPart, d.FileMinorPart, d.FileBuildPart, d.FilePrivatePart);
                return sv > dv;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static byte[] Download(string url, string accept)
        {
            // MusicBee may target an older framework whose default is TLS 1.0, which GitHub rejects.
            // Adding TLS 1.2 to the allowed set keeps every protocol the host already enabled.
            if ((ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12) == 0)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "MusicBee-DesktopLyrics-Romaji/" + CurrentVersion;
            request.Accept = accept;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 60000;
            request.AllowAutoRedirect = true;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        // ---- scheduling ----

        private static Timer _timer;
        private static readonly object Sync = new object();
        private static int _checking;

        /// <summary>Checks shortly after startup and then every 12 hours while MusicBee stays open.</summary>
        public static void Schedule(Action check)
        {
            lock (Sync)
            {
                _timer?.Dispose();
                _timer = new Timer(_ => check(), null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(12));
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        /// <summary>Prevents two checks (timer and a manual "Check now") from running at once.</summary>
        public static bool TryBeginCheck() => Interlocked.Exchange(ref _checking, 1) == 0;
        public static void EndCheck() => Interlocked.Exchange(ref _checking, 0);
    }
}
