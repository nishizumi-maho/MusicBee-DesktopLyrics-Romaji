using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NMeCab.Specialized;

namespace RomajiCore
{
    /// <summary>
    /// Converts Japanese text (kanji, hiragana, katakana) to Hepburn-style romaji using NMeCab + IPADIC.
    /// One tagger is shared by every plugin that loads this assembly.
    /// </summary>
    public static class Romanizer
    {
        private static readonly object Sync = new object();
        private static MeCabIpaDicTagger _tagger;
        private static bool _initFailed;
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        private static string _dictionaryDirectory;

        /// <summary>
        /// Folder holding sys.dic, matrix.bin, char.bin and unk.dic. Plugins set it from MusicBee's
        /// storage path; otherwise it is looked up next to this assembly.
        /// </summary>
        public static string DictionaryDirectory
        {
            get => _dictionaryDirectory ?? Path.Combine(Path.GetDirectoryName(typeof(Romanizer).Assembly.Location) ?? ".", "RomajiLyrics", "IpaDic");
            set { lock (Sync) { if (_tagger == null) { _dictionaryDirectory = value; _initFailed = false; } } }
        }

        /// <summary>
        /// Picks the first folder that contains the dictionary (sys.dic). Lets the same build work for
        /// installed MusicBee (%AppData%\MusicBee\Plugins) and portable MusicBee (&lt;MusicBee&gt;\Plugins).
        /// </summary>
        public static void UseFirstExistingDictionary(params string[] candidates)
        {
            foreach (var dir in candidates)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                if (File.Exists(Path.Combine(dir, "sys.dic"))) { DictionaryDirectory = dir; return; }
            }
        }

        /// <summary>Loads the dictionary in the background so the first lyric converts instantly.</summary>
        public static void Warmup()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { lock (Sync) EnsureTagger(); });
        }

        public static string LastError { get; private set; }

        private static bool EnsureTagger()
        {
            if (_tagger != null) return true;
            if (_initFailed) return false;
            try
            {
                _tagger = MeCabIpaDicTagger.Create(DictionaryDirectory);
                return true;
            }
            catch (Exception ex)
            {
                _initFailed = true;
                LastError = ex.ToString();
                return false;
            }
        }

        public static bool IsKana(char c) => (c >= 'ぁ' && c <= 'ゟ') || (c >= 'ァ' && c <= 'ヺ') || (c >= 'ㇰ' && c <= 'ㇿ');
        public static bool IsKanji(char c) => (c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || c == '々' || (c >= '豈' && c <= '﫿');

        public static bool HasKana(string s)
        {
            if (s == null) return false;
            foreach (var c in s) if (IsKana(c)) return true;
            return false;
        }

        public static bool HasJapaneseScript(string s)
        {
            if (s == null) return false;
            foreach (var c in s) if (IsKana(c) || IsKanji(c)) return true;
            return false;
        }

        /// <summary>
        /// True when a whole lyric text is Japanese (has a meaningful amount of kana).
        /// Chinese lyrics have kanji but no kana, so they are left alone.
        /// </summary>
        public static bool IsJapaneseText(IEnumerable<string> lines)
        {
            var kana = 0;
            foreach (var l in lines)
            {
                if (l == null) continue;
                foreach (var c in l) if (IsKana(c) && c != 'ー' && c != '・') kana++;
                if (kana >= 6) return true;
            }
            return false;
        }

        /// <summary>
        /// Romaji for one lyric line, or null if the line has nothing Japanese to convert.
        /// </summary>
        public static string ToRomaji(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !HasJapaneseScript(line)) return null;
            lock (Sync)
            {
                if (Cache.TryGetValue(line, out var cached)) return cached;
                if (!EnsureTagger()) return null;
                string result;
                try
                {
                    var normalized = line.Normalize(NormalizationForm.FormKC);
                    // MeCab drops whitespace, so convert each space-separated chunk on its own.
                    var chunks = normalized.Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries);
                    var parts = new List<string>();
                    foreach (var chunk in chunks)
                    {
                        var r = HasJapaneseScript(chunk) ? ConvertChunk(chunk) : chunk;
                        if (r.Length > 0) parts.Add(r);
                    }
                    result = string.Join(" ", parts);
                }
                catch (Exception ex)
                {
                    LastError = ex.ToString();
                    result = null;
                }
                if (Cache.Count > 20000) Cache.Clear();
                Cache[line] = result;
                return result;
            }
        }

        private static string ConvertChunk(string chunk)
        {
            var sb = new StringBuilder();
            var nodes = _tagger.Parse(chunk);
            var prevOpening = true; // no space at the start of a chunk
            string prevPos = null;
            foreach (var node in nodes)
            {
                var surface = node.Surface;
                if (string.IsNullOrEmpty(surface)) continue;
                var pos = node.PartsOfSpeech;
                var pos1 = node.PartsOfSpeechSection1;

                string roma;
                var isPunct = pos == "記号" || IsAllPunctuation(surface);
                if (isPunct)
                    roma = MapPunctuation(surface);
                else if (pos == "助詞" && surface == "は") roma = "wa";
                else if (pos == "助詞" && surface == "へ") roma = "e";
                else if (pos == "助詞" && surface == "を") roma = "o";
                else
                {
                    var reading = node.Reading;
                    if (string.IsNullOrEmpty(reading) || reading == "*")
                        roma = HasJapaneseScript(surface) ? KanaToRomaji(surface) : surface;
                    else
                        roma = KanaToRomaji(reading);
                    // Adverbs that IPADIC keeps as one word with a trailing に (本当に, どんなに) read
                    // more naturally in lyrics as "hontou ni", "donna ni".
                    if (pos == "副詞" && surface.Length > 2 && surface.EndsWith("に") && roma.EndsWith("ni") && roma.Length > 3)
                        roma = roma.Substring(0, roma.Length - 2) + " ni";
                }
                if (string.IsNullOrEmpty(roma)) { prevPos = pos; continue; }

                var closing = isPunct && IsClosingPunct(surface);
                var opening = isPunct && IsOpeningPunct(surface);
                var join = prevOpening || closing || JoinsPrevious(pos, pos1, surface, prevPos);

                // A sokuon at the end of the previous token doubles this token's first consonant.
                if (sb.Length > 0 && sb[sb.Length - 1] == '\u0001')
                {
                    sb.Length--;
                    roma = Geminate(roma);
                    join = true;
                }

                if (!join && sb.Length > 0) sb.Append(' ');
                sb.Append(roma);
                prevOpening = opening;
                prevPos = pos;
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == '\u0001') sb.Length--;
            return sb.ToString();
        }

        private static bool JoinsPrevious(string pos, string pos1, string surface, string prevPos)
        {
            if (prevPos == null) return false;
            switch (pos)
            {
                case "助動詞": return true;
                case "動詞": return pos1 == "非自立" || pos1 == "接尾";
                case "形容詞": return pos1 == "非自立" || pos1 == "接尾";
                case "名詞": return pos1 == "接尾" || (pos1 == "非自立" && (prevPos == "動詞" || prevPos == "助動詞"));
                case "助詞":
                    return pos1 == "接続助詞" && (surface == "て" || surface == "で" || surface == "ば" || surface == "ちゃ" || surface == "じゃ");
                default: return false;
            }
        }

        private static bool IsAllPunctuation(string s)
        {
            foreach (var c in s)
                if (char.IsLetterOrDigit(c) || IsKana(c) || IsKanji(c)) return false;
            return true;
        }

        private static bool IsOpeningPunct(string s) => s == "「" || s == "『" || s == "(" || s == "[" || s == "“" || s == "‘" || s == "\"";
        private static bool IsClosingPunct(string s) => !IsOpeningPunct(s) && s != "-" && s != "~" && s != "&";

        private static string MapPunctuation(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                switch (c)
                {
                    case '、': sb.Append(','); break;
                    case '。': sb.Append('.'); break;
                    case '「': case '『': sb.Append('“'); break;
                    case '」': case '』': sb.Append('”'); break;
                    case '・': sb.Append(' '); break;
                    case '…': case '‥': sb.Append("..."); break;
                    case '〜': sb.Append('~'); break;
                    case '♪': case '☆': case '★': case '♡': case '♥': sb.Append(c); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString().Trim();
        }

        // --- kana -> romaji -------------------------------------------------------------

        private static readonly Dictionary<string, string> Table = BuildTable();

        private static Dictionary<string, string> BuildTable()
        {
            var t = new Dictionary<string, string>();
            void Add(string kana, string roma) { t[kana] = roma; }
            string[] rows =
            {
                "ア a","イ i","ウ u","エ e","オ o",
                "カ ka","キ ki","ク ku","ケ ke","コ ko","ガ ga","ギ gi","グ gu","ゲ ge","ゴ go",
                "サ sa","シ shi","ス su","セ se","ソ so","ザ za","ジ ji","ズ zu","ゼ ze","ゾ zo",
                "タ ta","チ chi","ツ tsu","テ te","ト to","ダ da","ヂ ji","ヅ zu","デ de","ド do",
                "ナ na","ニ ni","ヌ nu","ネ ne","ノ no",
                "ハ ha","ヒ hi","フ fu","ヘ he","ホ ho","バ ba","ビ bi","ブ bu","ベ be","ボ bo","パ pa","ピ pi","プ pu","ペ pe","ポ po",
                "マ ma","ミ mi","ム mu","メ me","モ mo","ヤ ya","ユ yu","ヨ yo",
                "ラ ra","リ ri","ル ru","レ re","ロ ro","ワ wa","ヰ wi","ヱ we","ヲ o","ン n","ヴ vu",
                "ァ a","ィ i","ゥ u","ェ e","ォ o","ャ ya","ュ yu","ョ yo","ヮ wa","ヵ ka","ヶ ke",
                // yoon
                "キャ kya","キュ kyu","キョ kyo","ギャ gya","ギュ gyu","ギョ gyo",
                "シャ sha","シュ shu","ショ sho","ジャ ja","ジュ ju","ジョ jo",
                "チャ cha","チュ chu","チョ cho","ヂャ ja","ヂュ ju","ヂョ jo",
                "ニャ nya","ニュ nyu","ニョ nyo","ヒャ hya","ヒュ hyu","ヒョ hyo",
                "ビャ bya","ビュ byu","ビョ byo","ピャ pya","ピュ pyu","ピョ pyo",
                "ミャ mya","ミュ myu","ミョ myo","リャ rya","リュ ryu","リョ ryo",
                // extended katakana
                "シェ she","ジェ je","チェ che","ティ ti","ディ di","トゥ tu","ドゥ du","テュ tyu","デュ dyu",
                "ツァ tsa","ツィ tsi","ツェ tse","ツォ tso",
                "ファ fa","フィ fi","フェ fe","フォ fo","フュ fyu",
                "ウィ wi","ウェ we","ウォ wo","イェ ye",
                "ヴァ va","ヴィ vi","ヴェ ve","ヴォ vo","ヴュ vyu",
                "クァ kwa","グァ gwa"
            };
            foreach (var r in rows)
            {
                var p = r.Split(' ');
                Add(p[0], p[1]);
            }
            return t;
        }

        private static char ToKatakana(char c) => (c >= 'ぁ' && c <= 'ゖ') ? (char)(c + 0x60) : c;

        public static string KanaToRomaji(string kana)
        {
            var k = new StringBuilder(kana.Length);
            foreach (var c in kana) k.Append(ToKatakana(c));
            var s = k.ToString();
            var sb = new StringBuilder();
            var pendingSokuon = false;
            var lastWasN = false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == 'ッ') { pendingSokuon = true; continue; }
                if (c == 'ー')
                {
                    var v = LastVowel(sb);
                    if (v != '\0') sb.Append(v);
                    continue;
                }
                string roma = null;
                if (i + 1 < s.Length && Table.TryGetValue(s.Substring(i, 2), out var two)) { roma = two; i++; }
                else if (Table.TryGetValue(c.ToString(), out var one)) roma = one;

                if (roma == null)
                {
                    if (pendingSokuon) { pendingSokuon = false; }
                    sb.Append(c);
                    continue;
                }
                if (pendingSokuon) { roma = Geminate(roma); pendingSokuon = false; }
                // n before a vowel or y gets an apostrophe so "kan'i" is not read as "kani"
                if (lastWasN && "aiueoy".IndexOf(roma[0]) >= 0) sb.Append('\'');
                sb.Append(roma);
                lastWasN = c == 'ン';
            }
            if (pendingSokuon) sb.Append('\u0001'); // carried over to the next token
            return sb.ToString();
        }

        private static string Geminate(string roma)
        {
            if (string.IsNullOrEmpty(roma)) return roma;
            if (roma.StartsWith("ch")) return "t" + roma;
            var c = roma[0];
            if ("aiueon".IndexOf(c) >= 0 || !char.IsLetter(c)) return roma;
            return c + roma;
        }

        private static char LastVowel(StringBuilder sb)
        {
            for (var i = sb.Length - 1; i >= 0; i--)
                if ("aiueo".IndexOf(sb[i]) >= 0) return sb[i];
            return '\0';
        }
    }
}
