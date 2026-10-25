using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class LyricParser
    {
        private static readonly Regex LyricWordRegex = new Regex(@".*\](.*)", RegexOptions.Compiled);
        private static readonly Regex LyricTimeRegex = new Regex(@"\[([0-9.:]*)\]+(.*?)", RegexOptions.Compiled);
        private static readonly Regex LyricTimeSingleRegex = new Regex(@"(\d+):(\d+)(\.(\d+))?", RegexOptions.Compiled);
        // Enhanced LRC word stamp: <mm:ss.xx> (or .xxx) before each segment; a final stamp with no text
        // marks where the last segment ends.
        private static readonly Regex WordStampRegex = new Regex(@"<(\d+):(\d+)(?:[.:](\d{1,3}))?>", RegexOptions.Compiled);

        public static bool PreserveSlash { get; set; }

        // MusicBee often only has plain, untimed lyrics. Those still get displayed, one line every few seconds.
        private const int UntimedLineDurationMs = 3000;

        public static Lyrics ParseLyric(string lyric)
        {
            var lyricOffset = 0.0;
            if (lyric == null || lyric.Trim().Length == 0) return null;
            var rawLyrics = new List<RawLyricEntry>();
            var plainLines = new List<string>();
            using (var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(lyric))))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("["))
                    {
                        if (line.Trim().Length > 0) plainLines.Add(line.Trim());
                        continue;
                    }
                    if (line.StartsWith("[ti:") ||
                        line.StartsWith("[ar:") ||
                        line.StartsWith("[al:") ||
                        line.StartsWith("[by:")) continue;
                    if (line.StartsWith("[offset:"))
                    {
                        try
                        {
                            lyricOffset = ProcessOffset(line);
                        }
                        catch (Exception)
                        { /* IGNORED */ }
                        continue;
                    }
                    var wordMatch = LyricWordRegex.Match(line);
                    var word = wordMatch.Groups[1].Value;

                    var segments = ParseWordSegments(word);
                    if (segments != null) word = string.Concat(segments.Select(s => s.Text));

                    if (word.Length == 0) word = " ";

                    var timeMatch = LyricTimeRegex.Matches(line);

                    foreach (Match item in timeMatch)
                    {
                        var singleMatch = LyricTimeSingleRegex.Match(item.Groups[1].Value);
                        if (!singleMatch.Success)
                            continue;
                        if (!int.TryParse(singleMatch.Groups[1].Value, NumberStyles.Any, null, out var mins))
                            continue;
                        if (!int.TryParse(singleMatch.Groups[2].Value, NumberStyles.Any, null, out var secs))
                            continue;

                        var msec = 0;
                        if (singleMatch.Groups.Count > 4)
                        {
                            var fsecString = singleMatch.Groups[4].Value;
                            if (int.TryParse(fsecString, NumberStyles.Any, null, out msec))
                                msec *= fsecString.Length == 3 ? 1 : 10;
                        }

                        // Netease uses .MMM that provides milliseconds instead of 1/100s second
                        var time = mins * 60 * 1000 + secs * 1000 + msec;
                        rawLyrics.Add(new RawLyricEntry(time, word) { Segments = segments });
                    }
                }
            }

            if (rawLyrics.Count <= 0)
            {
                // No time tags at all: treat the text as a plain transcript and space the lines evenly.
                for (var i = 0; i < plainLines.Count; i++)
                    rawLyrics.Add(new RawLyricEntry(i * (double)UntimedLineDurationMs, plainLines[i]));
                if (rawLyrics.Count <= 0) return null;
            }
            var (entries, hasTranslation) = FoldLyricTranslation(rawLyrics);
            return new Lyrics
            {
                Entries = entries,
                Offset = lyricOffset,
                HasTranslation = hasTranslation,
                HasWordTimestamps = entries.Any(e => e.Segments != null)
            };
        }

        /// <summary>Segments of an enhanced LRC line body, or null when the body has no word stamps.
        /// Text between stamps is kept verbatim: no separator is inserted, so Japanese stays unspaced
        /// and Latin text carries its own spaces.</summary>
        private static List<WordSegment> ParseWordSegments(string body)
        {
            var marks = WordStampRegex.Matches(body);
            if (marks.Count == 0) return null;
            var segments = new List<WordSegment>();
            for (var i = 0; i < marks.Count; i++)
            {
                var m = marks[i];
                var end = i + 1 < marks.Count ? marks[i + 1].Index : body.Length;
                var text = body.Substring(m.Index + m.Length, end - m.Index - m.Length);
                var start = StampToMs(m);
                if (segments.Count > 0 && double.IsNaN(segments[segments.Count - 1].EndMs))
                    segments[segments.Count - 1].EndMs = start;
                if (text.Length > 0)
                    segments.Add(new WordSegment { Text = text, StartMs = start, EndMs = double.NaN });
            }
            if (segments.Count == 0) return null;
            // A line without a final stamp: the last segment ends when it starts (lit at once).
            var last = segments[segments.Count - 1];
            if (double.IsNaN(last.EndMs)) last.EndMs = last.StartMs;
            return segments;
        }

        private static double StampToMs(Match m)
        {
            var frac = m.Groups[3].Value;
            var ms = frac.Length == 0 ? 0 : int.Parse(frac.PadRight(3, '0'), CultureInfo.InvariantCulture);
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60000
                   + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 1000 + ms;
        }

        private static (List<LyricEntry>, bool) FoldLyricTranslation(IEnumerable<RawLyricEntry> rawLyrics)
        {
            var tableLine1 = new Dictionary<double, string>();
            var tableLine2 = new Dictionary<double, string>();
            var segments1 = new Dictionary<double, List<WordSegment>>();

            foreach (var rawLyricEntry in rawLyrics)
            {
                if (tableLine2.ContainsKey(rawLyricEntry.Time))
                    continue;
                if (!PreserveSlash && rawLyricEntry.LyricLine.Contains("/"))
                {
                    // Split into line + translation: the word stamps no longer describe line 1, drop them.
                    var segs = rawLyricEntry.LyricLine.Split(new[] { '/' }, 2);
                    tableLine1.Add(rawLyricEntry.Time, segs[0]);
                    tableLine2.Add(rawLyricEntry.Time, segs[1]);
                }
                else if (tableLine1.ContainsKey(rawLyricEntry.Time))
                    tableLine2.Add(rawLyricEntry.Time, rawLyricEntry.LyricLine);
                else
                {
                    tableLine1.Add(rawLyricEntry.Time, rawLyricEntry.LyricLine);
                    if (rawLyricEntry.Segments != null) segments1[rawLyricEntry.Time] = rawLyricEntry.Segments;
                }
            }

            var sortedTime = new ArrayList(tableLine1.Keys);
            sortedTime.Sort();

            var entries = new List<LyricEntry>();
            foreach (double time in sortedTime)
                entries.Add(new LyricEntry(time, tableLine1[time], (tableLine2.TryGetValue(time, out var line2) ? line2 : null))
                {
                    Segments = segments1.TryGetValue(time, out var s) ? s : null
                });
            return (entries, tableLine2.Count > 0);
        }


        private static double ProcessOffset(string line)
        {
            double.TryParse(line.Substring(line.IndexOf(':') + 1).TrimEnd(']'), out var offset);
            return offset;
        }

        public class LyricEntry
        {
            public double TimeMs { get; set; }
            public string LyricLine1 { get; set; }
            public string LyricLine2 { get; set; }
            /// <summary>Word timing of line 1 (enhanced LRC); null for line-timed entries.</summary>
            public List<WordSegment> Segments { get; set; }
            /// <summary>Word timing of line 2 when it is the romaji of line 1; null otherwise.</summary>
            public List<WordSegment> Segments2 { get; set; }

            public LyricEntry(double time, string lyricLine1, string lyricLine2)
            {
                TimeMs = time;
                LyricLine1 = lyricLine1;
                LyricLine2 = lyricLine2;
            }

            public override string ToString()
            {
                return "[Lyric time=" + TimeMs + " line1=" + LyricLine1 + " line2=" + LyricLine2 + "]\r";
            }

            public class LyricEntryComparer : Comparer<LyricEntry>
            {
                public override int Compare(LyricEntry x, LyricEntry y)
                {
                    if (x == null) return -1;
                    if (y == null) return 1;
                    return (int)(x.TimeMs - y.TimeMs);
                }
            }
        }

        public class RawLyricEntry
        {
            public double Time { get; set; }
            public string LyricLine { get; set; }
            public List<WordSegment> Segments { get; set; }

            public RawLyricEntry(double time, string lyricLine)
            {
                Time = time;
                LyricLine = lyricLine;
            }
        }

        public class WordSegment
        {
            public string Text { get; set; }
            public double StartMs { get; set; }
            public double EndMs { get; set; }
        }

        public class Lyrics
        {
            public List<LyricEntry> Entries; // Sorted guaranteed
            public double Offset;
            public bool HasTranslation;
            public bool HasWordTimestamps;
        }
    }
}
