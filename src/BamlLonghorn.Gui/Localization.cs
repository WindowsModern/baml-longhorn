using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace BamlLonghorn.Gui
{
    /// <summary>Every localizable string in the GUI.</summary>
    internal enum S
    {
        AppTitle,
        MenuFile,
        MenuOpenFolder,
        MenuOpenFile,
        MenuSaveXaml,
        MenuCopyView,
        MenuExit,
        MenuView,
        MenuReload,
        MenuLanguage,
        MenuLanguageAuto,
        ColFile,
        ColBytes,
        ColDialect,
        TabSummary,
        TabXaml,
        TabRecords,
        TabTree,
        TabTables,
        TabRecon,

        /// <summary>The conversion of the decompiled markup toward WPF.</summary>
        TabWpfXaml,
        Unrecognised,
        StatusReady,
        StatusFolder,
        StatusFile,
        StatusSaved,
        StatusCopied,
        StatusCopyFailed,
        StatusError,
        StatusLoadError,
        StatusRecords,
        StatusIncomplete,
        StatusBestScore,
        MenuFind,
        MenuFindNext,
        MenuFindPrevious,
        MenuExportAll,
        DlgExportAllTitle,
        DlgExportAllDone,
        StatusExportAllDone,
        MenuExpandBrushes,
        StatusFiltered,
        DlgChooseFolder,
        DlgChooseFolderDesc,
        DlgOpenFileTitle,
        DlgOpenFileFilter,
        DlgSaveXamlTitle,
        DlgSaveXamlFilter,
        DlgErrorTitle,
        TextUnrecognised,
        TextNoDocument,
        TextNoRecon,
        TextNoRecordLayer,
        TextXamlUnavailable,
        TextCannotDecompile
    }

    /// <summary>
    /// The GUI's string catalogue with runtime language switching.
    ///
    /// WHY NOT .resx SATELLITE ASSEMBLIES
    ///
    /// WinForms localisation via `Form.Localizable` + `Strings.xx.resx` cannot express
    /// script subtags: a file named `zh-Hans.resx` is rejected by the resource
    /// compiler, because the culture has to be one the runtime resolves to a
    /// satellite directory, and `zh-Hans` is a neutral-script tag rather than a
    /// specific culture. That is a real limitation for this project, whose supported
    /// list is written in terms of `zh-Hans` and `zh-Hant`.
    ///
    /// A plain table keeps the full tag vocabulary, gives exact control over the
    /// fallback chain, and makes the language switchable at runtime without a
    /// restart. Only the GUI is localised; the CLI stays English so its output can be
    /// diffed and scripted.
    /// </summary>
    internal static class Localization
    {
        private const string RegistryPath = @"Software\BamlLonghorn";
        private const string RegistryValue = "Language";

        /// <summary>Languages this build actually ships strings for.</summary>
        private static readonly string[] _translated =
        {
            "en-US", "zh-Hans", "zh-Hant", "ja-JP", "ko-KR", "de-DE", "fr-FR",
            "es-ES", "it-IT", "pt-BR", "pt-PT", "ru-RU", "pl-PL", "nl-NL",
            "sv-SE", "tr-TR", "cs-CZ", "hu-HU", "ar-SA", "he-IL", "th-TH",
            "vi-VN", "id-ID", "uk-UA"
        };

        /// <summary>
        /// Every tag the language menu offers, in the order it presents them.
        ///
        /// A tag present here but absent from <see cref="_translated"/> is still
        /// selectable and falls back to English, so the menu matches the product's
        /// supported-language list even where a translation is not written yet.
        /// </summary>
        public static readonly string[] SupportedLanguages =
        {
            "zh-CN", "en-US", "ar-SA", "bn-BD", "cs-CZ", "de-de", "el-GR", "en-au",
            "en-ca", "en-gb", "en-in", "es-es", "es-MX", "fa-IR", "fil-PH", "fr-ca",
            "fr-fr", "fr-mc", "he-IL", "hi-IN", "hu-HU", "id-ID", "it-it", "ja-jp",
            "ko-KR", "nl-NL", "pl-PL", "pt-br", "pt-PT", "ro-RO", "ru-RU", "sv-SE",
            "th-TH", "tr-TR", "uk-UA", "vi-VN", "zh-Hans", "zh-HK", "zh-TW", "zh-Hant"
        };

        /// <summary>
        /// Stored when the user has never picked a language, or has explicitly asked
        /// to follow Windows. Distinct from any real tag, so "no preference" is never
        /// confused with "chose en-US".
        /// </summary>
        public const string AutoTag = "auto";

        private static string _current;
        private static bool _followingSystem;
        private static Dictionary<S, string> _table;

        /// <summary>Raised after the language changes, so the UI can re-apply text.</summary>
        public static event EventHandler LanguageChanged;

        /// <summary>
        /// The active tag, e.g. "zh-Hans".
        ///
        /// The preference is read once from the registry. If nothing was ever stored,
        /// the GUI follows Windows and falls back to en-US when the system language
        /// has no translation. That is the intended out-of-box behaviour: start in the
        /// user's own language where one exists, English otherwise, and let the user
        /// override it explicitly.
        /// </summary>
        public static string CurrentTag
        {
            get
            {
                EnsureLoaded();
                return _current;
            }
        }

        /// <summary>True while no explicit language has been chosen.</summary>
        public static bool IsFollowingSystem
        {
            get
            {
                if (_current == null)
                {
                    string ignored = CurrentTag;   // forces the lazy load
                }
                return _followingSystem;
            }
        }

        /// <summary>The tag Windows itself selects, after the fallback is applied.</summary>
        public static string SystemTag
        {
            get { return Canonicalize(AutoTag); }
        }

        /// <summary>Display name of a tag, e.g. "Chinese (Simplified)".</summary>
        public static string DisplayName(string tag)
        {
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(MapToCulture(tag));
                string name = culture.EnglishName;
                // the invariant fallback shows up as an empty or invariant name
                if (string.IsNullOrEmpty(name) || name == "Invariant Language (Invariant Country)")
                {
                    return tag;
                }
                return name + "  [" + tag + "]";
            }
            catch (Exception)
            {
                return tag;
            }
        }

        /// <summary>True when this build ships a translation for the tag.</summary>
        public static bool HasTranslation(string tag)
        {
            string canonical = Canonicalize(tag);
            for (int i = 0; i < _translated.Length; i++)
            {
                if (string.Equals(_translated[i], canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Switches language and raises <see cref="LanguageChanged"/>.</summary>
        /// <summary>
        /// Switches language. Passing <see cref="AutoTag"/> returns to following
        /// Windows; any other value is an explicit choice and is remembered.
        /// </summary>
        public static void SetLanguage(string tag)
        {
            _followingSystem = string.IsNullOrEmpty(tag)
                || tag.Equals(AutoTag, StringComparison.OrdinalIgnoreCase);
            string canonical = Canonicalize(_followingSystem ? AutoTag : tag);
            _current = canonical;
            _table = Build(canonical);
            Save(_followingSystem ? AutoTag : canonical);
            EventHandler handler = LanguageChanged;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// The string for a key in the active language.
        ///
        /// This MUST ensure the table is loaded. Relying on <see cref="CurrentTag"/>
        /// having been read first produced a real defect: the form's own constructor
        /// calls T() before anything touches CurrentTag, so every caption it set fell
        /// through to English while the language menu -- which does read CurrentTag --
        /// showed the correct language. The window was half-translated.
        /// </summary>
        public static string T(S key)
        {
            EnsureLoaded();
            string value;
            if (_table.TryGetValue(key, out value) && value != null)
            {
                return value;
            }
            return English(key);
        }

        /// <summary>Loads the table once, whatever entry point is used first.</summary>
        private static void EnsureLoaded()
        {
            if (_current == null)
            {
                _current = Canonicalize(LoadStoredPreference());
                _table = Build(_current);
            }
        }

        /// <summary>Format with the current culture, for messages carrying numbers.</summary>
        public static string T(S key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }

        // ------------------------------------------------------------------
        // tag handling
        // ------------------------------------------------------------------

        /// <summary>
        /// Maps a supported tag onto the tag whose strings are used.
        ///
        /// Regional variants fold onto their base translation, and the script tags
        /// the product list uses fold onto a concrete culture that carries the same
        /// reading. Anything unknown falls back to en-US rather than throwing, so a
        /// hand-edited registry value can never stop the GUI from starting.
        /// </summary>
        public static string Canonicalize(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return FromSystemCulture(CultureInfo.CurrentUICulture);
            }

            string lower = tag.ToLowerInvariant();

            // script tags: the reading is what matters, so pin them explicitly
            if (lower == "zh-hans" || lower == "zh-cn" || lower == "zh-sg") return "zh-Hans";
            if (lower == "zh-hant" || lower == "zh-tw" || lower == "zh-hk" || lower == "zh-mo")
            {
                return "zh-Hant";
            }

            // fold regional variants onto the base translation
            if (lower.StartsWith("en-", StringComparison.Ordinal)) return "en-US";
            if (lower.StartsWith("de-", StringComparison.Ordinal)) return "de-DE";
            if (lower.StartsWith("fr-", StringComparison.Ordinal)) return "fr-FR";
            if (lower.StartsWith("es-", StringComparison.Ordinal)) return "es-ES";
            if (lower.StartsWith("it-", StringComparison.Ordinal)) return "it-IT";
            if (lower.StartsWith("pt-", StringComparison.Ordinal)) return lower == "pt-br" ? "pt-BR" : "pt-PT";
            if (lower.StartsWith("ru-", StringComparison.Ordinal)) return "ru-RU";
            if (lower.StartsWith("pl-", StringComparison.Ordinal)) return "pl-PL";
            if (lower.StartsWith("nl-", StringComparison.Ordinal)) return "nl-NL";
            if (lower.StartsWith("sv-", StringComparison.Ordinal)) return "sv-SE";
            if (lower.StartsWith("tr-", StringComparison.Ordinal)) return "tr-TR";
            if (lower.StartsWith("cs-", StringComparison.Ordinal)) return "cs-CZ";
            if (lower.StartsWith("hu-", StringComparison.Ordinal)) return "hu-HU";
            if (lower.StartsWith("ar-", StringComparison.Ordinal)) return "ar-SA";
            if (lower.StartsWith("he-", StringComparison.Ordinal)) return "he-IL";
            if (lower.StartsWith("th-", StringComparison.Ordinal)) return "th-TH";
            if (lower.StartsWith("vi-", StringComparison.Ordinal)) return "vi-VN";
            if (lower.StartsWith("id-", StringComparison.Ordinal)) return "id-ID";
            if (lower.StartsWith("uk-", StringComparison.Ordinal)) return "uk-UA";
            if (lower.StartsWith("ja", StringComparison.Ordinal)) return "ja-JP";
            if (lower.StartsWith("ko", StringComparison.Ordinal)) return "ko-KR";

            // exact tag that has a table
            for (int i = 0; i < _translated.Length; i++)
            {
                if (string.Equals(_translated[i], lower, StringComparison.OrdinalIgnoreCase))
                {
                    return _translated[i];
                }
            }
            return "en-US";
        }

        private static string FromSystemCulture(CultureInfo culture)
        {
            try
            {
                return Canonicalize(culture.Name);
            }
            catch (Exception)
            {
                return "en-US";
            }
        }

        /// <summary>
        /// The right-to-left languages this table offers. Arabic and Hebrew are the only
        /// two, so the set is written out rather than inferred from the culture: a
        /// CultureInfo's TextInfo does not reliably report direction on .NET Framework,
        /// and guessing from the script would be less predictable than listing them.
        /// </summary>
        private static readonly string[] _rtl = new string[] { "ar-SA", "he-IL" };

        /// <summary>
        /// True when the active language is written right to left, so the form should
        /// mirror its layout.
        /// </summary>
        public static bool IsRightToLeft
        {
            get
            {
                string tag = CurrentTag;
                for (int i = 0; i < _rtl.Length; i++)
                {
                    if (string.Equals(_rtl[i], tag, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// A culture suitable for <see cref="CultureInfo"/>, which rejects the script
        /// subtags the product list uses.
        /// </summary>
        private static string MapToCulture(string tag)
        {
            if (tag == null) return "en-US";
            if (tag.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (tag.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            if (tag.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            return tag;
        }

        // ------------------------------------------------------------------
        // persistence
        // ------------------------------------------------------------------

        /// <summary>
        /// The stored preference, or <see cref="AutoTag"/> when the user has never
        /// chosen one.
        ///
        /// Deliberately does NOT resolve "auto" to a concrete tag here: returning the
        /// sentinel lets <see cref="IsFollowingSystem"/> distinguish "never chose" from
        /// "chose en-US", which the menu needs in order to tick the right entry. A
        /// locked-down or absent registry is treated as "no preference" rather than
        /// being allowed to prevent startup.
        /// </summary>
        private static string LoadStoredPreference()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(RegistryValue);
                        if (value is string && ((string)value).Length > 0)
                        {
                            string stored = (string)value;
                            if (stored.Equals(AutoTag, StringComparison.OrdinalIgnoreCase))
                            {
                                _followingSystem = true;
                                return AutoTag;
                            }
                            _followingSystem = false;
                            return stored;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            _followingSystem = true;
            return AutoTag;
        }

        private static void Save(string tag)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        key.SetValue(RegistryValue, tag, RegistryValueKind.String);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------
        // tables
        // ------------------------------------------------------------------

        private static Dictionary<S, string> Build(string tag)
        {
            string blob = Blob(tag);
            if (blob == null)
            {
                blob = Blob("en-US");
            }
            return Parse(blob, tag);
        }

        /// <summary>
        /// Parses a "key=value" blob. Missing keys fall back to English, which is what
        /// lets a partially translated language ship without holes.
        /// </summary>
        private static Dictionary<S, string> Parse(string blob, string tag)
        {
            Dictionary<S, string> map = new Dictionary<S, string>();
            if (tag != "en-US")
            {
                ParseInto(Blob("en-US"), map);      // baseline first
            }
            ParseInto(blob, map);                   // then override
            return map;
        }

        private static void ParseInto(string blob, Dictionary<S, string> map)
        {
            if (blob == null)
            {
                return;
            }
            string[] lines = blob.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string name = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                S key;
                if (Enum.TryParse<S>(name, out key))
                {
                    map[key] = Unescape(value);
                }
            }
        }

        /// <summary>Turns the literal \n \t sequences in a table into real characters.</summary>
        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                    if (next == 't') { sb.Append('\t'); i++; continue; }
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(value[i]);
            }
            return sb.ToString();
        }

        /// <summary>The English text, used as the fallback for every language.</summary>
        private static Dictionary<S, string> _english;

        private static string English(S key)
        {
            if (_english == null)
            {
                Dictionary<S, string> map = new Dictionary<S, string>();
                ParseInto(Blob("en-US"), map);
                _english = map;
            }
            string value;
            return _english.TryGetValue(key, out value) ? value : key.ToString();
        }

        private static string Blob(string tag)
        {
            switch (tag)
            {
                case "en-US": return EnUs;
                case "zh-Hans": return ZhHans;
                case "zh-Hant": return ZhHant;
                case "ja-JP": return JaJp;
                case "ko-KR": return KoKr;
                case "de-DE": return DeDe;
                case "fr-FR": return FrFr;
                case "es-ES": return EsEs;
                case "it-IT": return ItIt;
                case "pt-BR": return PtBr;
                case "pt-PT": return PtPt;
                case "ru-RU": return RuRu;
                case "pl-PL": return PlPl;
                case "nl-NL": return NlNl;
                case "sv-SE": return SvSe;
                case "tr-TR": return TrTr;
                case "cs-CZ": return CsCz;
                case "hu-HU": return HuHu;
                case "ar-SA": return ArSa;
                case "he-IL": return HeIl;
                case "th-TH": return ThTh;
                case "vi-VN": return ViVn;
                case "id-ID": return IdId;
                case "uk-UA": return UkUa;
                default: return null;
            }
        }

        // ------------------------------------------------------------------
        // the tables themselves
        // ------------------------------------------------------------------

        private const string EnUs = @"
AppTitle=BamlLonghorn - Longhorn BAML decompiler
MenuFile=&File
MenuOpenFolder=&Open folder...
MenuOpenFile=Open &file...
MenuSaveXaml=&Save XAML as...
MenuCopyView=&Copy current view
MenuExit=E&xit
MenuView=&View
MenuReload=&Reload
MenuLanguage=&Language
MenuLanguageAuto=(follow Windows)
ColFile=file
ColBytes=bytes
ColDialect=dialect
TabSummary=Summary
TabXaml=XAML
TabRecords=Records
TabTree=Tree
TabTables=Tables
TabRecon=Recon
TabWpfXaml=WPF XAML
Unrecognised=unrecognised
StatusReady=Open a folder or a .baml file to begin.
StatusFolder={0}   ({1} file(s))
StatusFile={0}   {1} bytes   {2}
StatusSaved=saved {0}
StatusCopied=copied {0} characters
StatusCopyFailed=copy failed: {0}
StatusError=error: {0}
StatusLoadError=load error: {0}
StatusRecords={0} record(s)
StatusIncomplete=[INCOMPLETE]
StatusBestScore=(best score {0}%)
MenuFind=&Find...
MenuFindNext=Find &next
MenuFindPrevious=Find &previous
MenuExportAll=&Export all XAML...
DlgExportAllTitle=Choose a folder to receive the decompiled XAML
DlgExportAllDone=exported {0} file(s), {1} skipped
StatusExportAllDone=exported {0} file(s), {1} skipped
MenuExpandBrushes=Expand compound &brushes
StatusFiltered={0} of {1} line(s)
DlgExportAllOk=(export only works for decoded generations)
DlgChooseFolder=Choose a folder containing .baml files
DlgChooseFolderDesc=Choose a folder containing .baml files
DlgOpenFileTitle=Open a BAML file
DlgOpenFileFilter=BAML (*.baml)|*.baml|All files (*.*)|*.*
DlgSaveXamlTitle=Save decompiled XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Text (*.txt)|*.txt|All files (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=stream is not recognised as any supported BAML generation.
TextNoDocument=(no document)
TextNoRecon=(no reconnaissance data for this dialect)
TextNoRecordLayer=this dialect's record layer is not decoded, so XAML output is not available (see Records or Recon).
TextXamlUnavailable=XAML output requires a record-level decode, which this dialect does not provide yet.
TextCannotDecompile=cannot decompile: {0}
";

        private const string ZhHans = @"
AppTitle=BamlLonghorn - Longhorn BAML 反编译器
MenuFile=文件(&F)
MenuOpenFolder=打开文件夹(&O)...
MenuOpenFile=打开文件(&F)...
MenuSaveXaml=另存为 XAML(&S)...
MenuCopyView=复制当前视图(&C)
MenuExit=退出(&X)
MenuView=视图(&V)
MenuReload=重新加载(&R)
MenuLanguage=语言(&L)
MenuFind=查找(&F)...
MenuFindNext=查找下一个(&N)
MenuFindPrevious=查找上一个(&P)
MenuExportAll=导出全部 XAML(&E)...
DlgExportAllTitle=选择接收反编译 XAML 的文件夹
DlgExportAllDone=已导出 {0} 个文件，跳过 {1} 个
StatusExportAllDone=已导出 {0} 个文件，跳过 {1} 个
MenuExpandBrushes=展开复合画刷(&B)
StatusFiltered={1} 行中的 {0} 行
MenuLanguageAuto=(跟随 Windows)
ColFile=文件
ColBytes=字节
ColDialect=方言
TabSummary=摘要
TabXaml=XAML
TabRecords=记录
TabTree=树
TabTables=驻留表
TabRecon=侦察
TabWpfXaml=转为 WPF
Unrecognised=未识别
StatusReady=请打开一个文件夹或 .baml 文件。
StatusFolder={0}   （{1} 个文件）
StatusFile={0}   {1} 字节   {2}
StatusSaved=已保存 {0}
StatusCopied=已复制 {0} 个字符
StatusCopyFailed=复制失败：{0}
StatusError=错误：{0}
StatusLoadError=加载错误：{0}
StatusRecords={0} 条记录
StatusIncomplete=[不完整]
StatusBestScore=（最高得分 {0}%）
DlgChooseFolder=选择包含 .baml 文件的文件夹
DlgChooseFolderDesc=选择包含 .baml 文件的文件夹
DlgOpenFileTitle=打开 BAML 文件
DlgOpenFileFilter=BAML (*.baml)|*.baml|所有文件 (*.*)|*.*
DlgSaveXamlTitle=保存反编译的 XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|文本 (*.txt)|*.txt|所有文件 (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=该数据流不属于任何受支持的 BAML 世代。
TextNoDocument=（无文档）
TextNoRecon=（此方言没有侦察数据）
TextNoRecordLayer=此方言的记录层尚未解码，因此无法输出 XAML（请查看「记录」或「侦察」）。
TextXamlUnavailable=XAML 输出需要记录级解码，此方言尚未提供。
TextCannotDecompile=无法反编译：{0}
";

        private const string ZhHant = @"
AppTitle=BamlLonghorn - Longhorn BAML 反編譯器
MenuFile=檔案(&F)
MenuOpenFolder=開啟資料夾(&O)...
MenuOpenFile=開啟檔案(&F)...
MenuSaveXaml=另存為 XAML(&S)...
MenuCopyView=複製目前檢視(&C)
MenuExit=結束(&X)
MenuView=檢視(&V)
MenuReload=重新載入(&R)
MenuLanguage=語言(&L)
MenuLanguageAuto=(跟隨 Windows)
ColFile=檔案
ColBytes=位元組
ColDialect=方言
TabSummary=摘要
TabRecords=記錄
TabTree=樹狀結構
TabTables=駐留表
TabRecon=偵察
TabWpfXaml=轉為 WPF
Unrecognised=未識別
StatusReady=請開啟資料夾或 .baml 檔案。
StatusFolder={0}   （{1} 個檔案）
StatusFile={0}   {1} 位元組   {2}
StatusSaved=已儲存 {0}
StatusCopied=已複製 {0} 個字元
StatusCopyFailed=複製失敗：{0}
StatusError=錯誤：{0}
StatusLoadError=載入錯誤：{0}
StatusRecords={0} 筆記錄
StatusIncomplete=[不完整]
StatusBestScore=（最高分數 {0}%）
DlgChooseFolder=選擇包含 .baml 檔案的資料夾
DlgChooseFolderDesc=選擇包含 .baml 檔案的資料夾
DlgOpenFileTitle=開啟 BAML 檔案
DlgOpenFileFilter=BAML (*.baml)|*.baml|所有檔案 (*.*)|*.*
DlgSaveXamlTitle=儲存反編譯的 XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|文字 (*.txt)|*.txt|所有檔案 (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=此資料流不屬於任何支援的 BAML 世代。
TextNoDocument=（無文件）
TextNoRecon=（此方言沒有偵察資料）
TextNoRecordLayer=此方言的記錄層尚未解碼，因此無法輸出 XAML（請參閱「記錄」或「偵察」）。
TextXamlUnavailable=XAML 輸出需要記錄層級解碼，此方言尚未提供。
TextCannotDecompile=無法反編譯：{0}
";

        private const string JaJp = @"
AppTitle=BamlLonghorn - Longhorn BAML 逆コンパイラ
MenuFile=ファイル(&F)
MenuOpenFolder=フォルダーを開く(&O)...
MenuOpenFile=ファイルを開く(&F)...
MenuSaveXaml=XAML として保存(&S)...
MenuCopyView=現在のビューをコピー(&C)
MenuExit=終了(&X)
MenuView=表示(&V)
MenuReload=再読み込み(&R)
MenuLanguage=言語(&L)
MenuLanguageAuto=(Windows に従う)
ColFile=ファイル
ColBytes=バイト
ColDialect=方言
TabSummary=概要
TabRecords=レコード
TabTree=ツリー
TabTables=インターンテーブル
TabRecon=調査
TabWpfXaml=WPF 変換
Unrecognised=未認識
StatusReady=フォルダーまたは .baml ファイルを開いてください。
StatusFolder={0}   ({1} 個のファイル)
StatusFile={0}   {1} バイト   {2}
StatusSaved={0} を保存しました
StatusCopied={0} 文字をコピーしました
StatusCopyFailed=コピーに失敗しました: {0}
StatusError=エラー: {0}
StatusLoadError=読み込みエラー: {0}
StatusRecords={0} 件のレコード
StatusIncomplete=[不完全]
StatusBestScore=(最高スコア {0}%)
DlgChooseFolder=.baml ファイルを含むフォルダーを選択してください
DlgChooseFolderDesc=.baml ファイルを含むフォルダーを選択してください
DlgOpenFileTitle=BAML ファイルを開く
DlgOpenFileFilter=BAML (*.baml)|*.baml|すべてのファイル (*.*)|*.*
DlgSaveXamlTitle=逆コンパイルした XAML を保存
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=このストリームは対応する BAML 世代ではありません。
TextNoDocument=(ドキュメントなし)
TextNoRecon=(この方言には調査データがありません)
TextNoRecordLayer=この方言のレコード層は未解読のため、XAML を出力できません (「レコード」または「調査」を参照)。
TextXamlUnavailable=XAML 出力にはレコードレベルの解読が必要ですが、この方言では未対応です。
TextCannotDecompile=逆コンパイルできません: {0}
";

        private const string KoKr = @"
AppTitle=BamlLonghorn - Longhorn BAML 디컴파일러
MenuFile=파일(&F)
MenuOpenFolder=폴더 열기(&O)...
MenuOpenFile=파일 열기(&F)...
MenuSaveXaml=XAML로 저장(&S)...
MenuCopyView=현재 보기 복사(&C)
MenuExit=끝내기(&X)
MenuView=보기(&V)
MenuReload=다시 로드(&R)
MenuLanguage=언어(&L)
MenuLanguageAuto=(Windows 따름)
ColFile=파일
ColBytes=바이트
ColDialect=방언
TabSummary=요약
TabRecords=레코드
TabTree=트리
TabTables=인턴 테이블
TabRecon=정찰
TabWpfXaml=WPF XAML
Unrecognised=인식되지 않음
StatusReady=폴더 또는 .baml 파일을 여십시오.
StatusFolder={0}   (파일 {1}개)
StatusFile={0}   {1} 바이트   {2}
StatusSaved={0} 저장됨
StatusCopied={0}자 복사됨
StatusCopyFailed=복사 실패: {0}
StatusError=오류: {0}
StatusLoadError=로드 오류: {0}
StatusRecords=레코드 {0}개
StatusIncomplete=[불완전]
StatusBestScore=(최고 점수 {0}%)
DlgChooseFolder=.baml 파일이 있는 폴더를 선택하십시오
DlgChooseFolderDesc=.baml 파일이 있는 폴더를 선택하십시오
DlgOpenFileTitle=BAML 파일 열기
DlgOpenFileFilter=BAML (*.baml)|*.baml|모든 파일 (*.*)|*.*
DlgSaveXamlTitle=디컴파일된 XAML 저장
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|텍스트 (*.txt)|*.txt|모든 파일 (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=이 스트림은 지원되는 BAML 세대가 아닙니다.
TextNoDocument=(문서 없음)
TextNoRecon=(이 방언에는 정찰 데이터가 없습니다)
TextNoRecordLayer=이 방언의 레코드 계층이 해독되지 않아 XAML을 출력할 수 없습니다(레코드 또는 정찰 참조).
TextXamlUnavailable=XAML 출력에는 레코드 수준 해독이 필요하지만 이 방언은 아직 지원하지 않습니다.
TextCannotDecompile=디컴파일할 수 없습니다: {0}
";

        private const string DeDe = @"
AppTitle=BamlLonghorn - Longhorn-BAML-Dekompiler
MenuFile=&Datei
MenuOpenFolder=Ordner &öffnen...
MenuOpenFile=&Datei öffnen...
MenuSaveXaml=XAML &speichern unter...
MenuCopyView=Aktuelle Ansicht &kopieren
MenuExit=&Beenden
MenuView=&Ansicht
MenuReload=&Neu laden
MenuLanguage=&Sprache
MenuLanguageAuto=(Windows folgen)
ColFile=Datei
ColBytes=Bytes
ColDialect=Dialekt
TabSummary=Übersicht
TabRecords=Datensätze
TabTree=Baum
TabTables=Internierungstabellen
TabRecon=Aufklärung
TabWpfXaml=WPF-XAML
Unrecognised=unbekannt
StatusReady=Öffnen Sie einen Ordner oder eine .baml-Datei.
StatusFolder={0}   ({1} Datei(en))
StatusFile={0}   {1} Bytes   {2}
StatusSaved={0} gespeichert
StatusCopied={0} Zeichen kopiert
StatusCopyFailed=Kopieren fehlgeschlagen: {0}
StatusError=Fehler: {0}
StatusLoadError=Ladefehler: {0}
StatusRecords={0} Datensatz/Datensätze
StatusIncomplete=[UNVOLLSTÄNDIG]
StatusBestScore=(Bestwert {0}%)
DlgChooseFolder=Ordner mit .baml-Dateien auswählen
DlgChooseFolderDesc=Ordner mit .baml-Dateien auswählen
DlgOpenFileTitle=BAML-Datei öffnen
DlgOpenFileFilter=BAML (*.baml)|*.baml|Alle Dateien (*.*)|*.*
DlgSaveXamlTitle=Dekompiliertes XAML speichern
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Text (*.txt)|*.txt|Alle Dateien (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Dieser Stream gehört keiner unterstützten BAML-Generation an.
TextNoDocument=(kein Dokument)
TextNoRecon=(keine Aufklärungsdaten für diesen Dialekt)
TextNoRecordLayer=Die Datensatzebene dieses Dialekts ist nicht dekodiert, daher ist keine XAML-Ausgabe möglich (siehe Datensätze oder Aufklärung).
TextXamlUnavailable=Die XAML-Ausgabe erfordert eine Dekodierung auf Datensatzebene, die dieser Dialekt noch nicht bietet.
TextCannotDecompile=Dekompilierung nicht möglich: {0}
";

        private const string FrFr = @"
AppTitle=BamlLonghorn - décompilateur BAML Longhorn
MenuFile=&Fichier
MenuOpenFolder=&Ouvrir un dossier...
MenuOpenFile=Ouvrir un &fichier...
MenuSaveXaml=&Enregistrer le XAML sous...
MenuCopyView=&Copier la vue actuelle
MenuExit=&Quitter
MenuView=&Affichage
MenuReload=&Recharger
MenuLanguage=&Langue
MenuLanguageAuto=(suivre Windows)
ColFile=fichier
ColBytes=octets
ColDialect=dialecte
TabSummary=Résumé
TabRecords=Enregistrements
TabTree=Arborescence
TabTables=Tables d'internement
TabRecon=Reconnaissance
TabWpfXaml=XAML WPF
Unrecognised=non reconnu
StatusReady=Ouvrez un dossier ou un fichier .baml.
StatusFolder={0}   ({1} fichier(s))
StatusFile={0}   {1} octets   {2}
StatusSaved={0} enregistré
StatusCopied={0} caractères copiés
StatusCopyFailed=échec de la copie : {0}
StatusError=erreur : {0}
StatusLoadError=erreur de chargement : {0}
StatusRecords={0} enregistrement(s)
StatusIncomplete=[INCOMPLET]
StatusBestScore=(meilleur score {0} %)
DlgChooseFolder=Choisissez un dossier contenant des fichiers .baml
DlgChooseFolderDesc=Choisissez un dossier contenant des fichiers .baml
DlgOpenFileTitle=Ouvrir un fichier BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Tous les fichiers (*.*)|*.*
DlgSaveXamlTitle=Enregistrer le XAML décompilé
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Ce flux n'appartient à aucune génération BAML prise en charge.
TextNoDocument=(aucun document)
TextNoRecon=(aucune donnée de reconnaissance pour ce dialecte)
TextNoRecordLayer=La couche d'enregistrements de ce dialecte n'est pas décodée ; la sortie XAML est donc indisponible (voir Enregistrements ou Reconnaissance).
TextXamlUnavailable=La sortie XAML nécessite un décodage au niveau des enregistrements, que ce dialecte ne fournit pas encore.
TextCannotDecompile=décompilation impossible : {0}
";

        private const string EsEs = @"
AppTitle=BamlLonghorn - descompilador BAML de Longhorn
MenuFile=&Archivo
MenuOpenFolder=&Abrir carpeta...
MenuOpenFile=Abrir &archivo...
MenuSaveXaml=&Guardar XAML como...
MenuCopyView=&Copiar vista actual
MenuExit=&Salir
MenuView=&Ver
MenuReload=&Recargar
MenuLanguage=&Idioma
MenuLanguageAuto=(seguir a Windows)
ColFile=archivo
ColBytes=bytes
ColDialect=dialecto
TabSummary=Resumen
TabRecords=Registros
TabTree=Árbol
TabTables=Tablas de internamiento
TabRecon=Reconocimiento
TabWpfXaml=XAML de WPF
Unrecognised=no reconocido
StatusReady=Abra una carpeta o un archivo .baml.
StatusFolder={0}   ({1} archivo(s))
StatusFile={0}   {1} bytes   {2}
StatusSaved={0} guardado
StatusCopied={0} caracteres copiados
StatusCopyFailed=error al copiar: {0}
StatusError=error: {0}
StatusLoadError=error de carga: {0}
StatusRecords={0} registro(s)
StatusIncomplete=[INCOMPLETO]
StatusBestScore=(mejor puntuación {0} %)
DlgChooseFolder=Elija una carpeta que contenga archivos .baml
DlgChooseFolderDesc=Elija una carpeta que contenga archivos .baml
DlgOpenFileTitle=Abrir un archivo BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Todos los archivos (*.*)|*.*
DlgSaveXamlTitle=Guardar el XAML descompilado
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Este flujo no pertenece a ninguna generación BAML compatible.
TextNoDocument=(sin documento)
TextNoRecon=(no hay datos de reconocimiento para este dialecto)
TextNoRecordLayer=La capa de registros de este dialecto no está descodificada, por lo que no hay salida XAML (véase Registros o Reconocimiento).
TextXamlUnavailable=La salida XAML requiere una descodificación a nivel de registro, que este dialecto aún no ofrece.
TextCannotDecompile=no se puede descompilar: {0}
";

        private const string ItIt = @"
AppTitle=BamlLonghorn - decompilatore BAML per Longhorn
MenuFile=&File
MenuOpenFolder=&Apri cartella...
MenuOpenFile=Apri &file...
MenuSaveXaml=&Salva XAML con nome...
MenuCopyView=&Copia vista corrente
MenuExit=&Esci
MenuView=&Visualizza
MenuReload=&Ricarica
MenuLanguage=&Lingua
MenuLanguageAuto=(segui Windows)
ColFile=file
ColBytes=byte
ColDialect=dialetto
TabSummary=Riepilogo
TabRecords=Record
TabTree=Albero
TabTables=Tabelle di internamento
TabRecon=Ricognizione
TabWpfXaml=XAML WPF
Unrecognised=non riconosciuto
StatusReady=Aprire una cartella o un file .baml.
StatusFolder={0}   ({1} file)
StatusFile={0}   {1} byte   {2}
StatusSaved={0} salvato
StatusCopied={0} caratteri copiati
StatusCopyFailed=copia non riuscita: {0}
StatusError=errore: {0}
StatusLoadError=errore di caricamento: {0}
StatusRecords={0} record
StatusIncomplete=[INCOMPLETO]
StatusBestScore=(punteggio migliore {0}%)
DlgChooseFolder=Scegliere una cartella contenente file .baml
DlgChooseFolderDesc=Scegliere una cartella contenente file .baml
DlgOpenFileTitle=Apri un file BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Tutti i file (*.*)|*.*
DlgSaveXamlTitle=Salva il XAML decompilato
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Testo (*.txt)|*.txt|Tutti i file (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Questo flusso non appartiene ad alcuna generazione BAML supportata.
TextNoDocument=(nessun documento)
TextNoRecon=(nessun dato di ricognizione per questo dialetto)
TextNoRecordLayer=Il livello dei record di questo dialetto non è decodificato, quindi l'output XAML non è disponibile (vedere Record o Ricognizione).
TextXamlUnavailable=L'output XAML richiede una decodifica a livello di record, che questo dialetto non fornisce ancora.
TextCannotDecompile=impossibile decompilare: {0}
";

        private const string PtBr = @"
AppTitle=BamlLonghorn - descompilador BAML do Longhorn
MenuFile=&Arquivo
MenuOpenFolder=&Abrir pasta...
MenuOpenFile=Abrir &arquivo...
MenuSaveXaml=&Salvar XAML como...
MenuCopyView=&Copiar exibição atual
MenuExit=&Sair
MenuView=&Exibir
MenuReload=&Recarregar
MenuLanguage=&Idioma
MenuLanguageAuto=(seguir o Windows)
ColFile=arquivo
ColBytes=bytes
ColDialect=dialeto
TabSummary=Resumo
TabRecords=Registros
TabTree=Árvore
TabTables=Tabelas de interning
TabRecon=Reconhecimento
TabWpfXaml=XAML para WPF
Unrecognised=não reconhecido
StatusReady=Abra uma pasta ou um arquivo .baml.
StatusFolder={0}   ({1} arquivo(s))
StatusFile={0}   {1} bytes   {2}
StatusSaved={0} salvo
StatusCopied={0} caracteres copiados
StatusCopyFailed=falha ao copiar: {0}
StatusError=erro: {0}
StatusLoadError=erro de carregamento: {0}
StatusRecords={0} registro(s)
StatusIncomplete=[INCOMPLETO]
StatusBestScore=(melhor pontuação {0}%)
DlgChooseFolder=Escolha uma pasta que contenha arquivos .baml
DlgChooseFolderDesc=Escolha uma pasta que contenha arquivos .baml
DlgOpenFileTitle=Abrir um arquivo BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Todos os arquivos (*.*)|*.*
DlgSaveXamlTitle=Salvar o XAML descompilado
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Este fluxo não pertence a nenhuma geração BAML compatível.
TextNoDocument=(sem documento)
TextNoRecon=(sem dados de reconhecimento para este dialeto)
TextNoRecordLayer=A camada de registros deste dialeto não está decodificada, portanto a saída XAML não está disponível (ver Registros ou Reconhecimento).
TextXamlUnavailable=A saída XAML exige decodificação em nível de registro, que este dialeto ainda não oferece.
TextCannotDecompile=não é possível descompilar: {0}
";

        private const string PtPt = @"
AppTitle=BamlLonghorn - descompilador BAML do Longhorn
MenuFile=&Ficheiro
MenuOpenFolder=&Abrir pasta...
MenuOpenFile=Abrir &ficheiro...
MenuSaveXaml=&Guardar XAML como...
MenuCopyView=&Copiar vista atual
MenuExit=&Sair
MenuView=&Ver
MenuReload=&Recarregar
MenuLanguage=&Idioma
MenuLanguageAuto=(seguir o Windows)
ColFile=ficheiro
ColBytes=bytes
ColDialect=dialeto
TabSummary=Resumo
TabRecords=Registos
TabTree=Árvore
TabTables=Tabelas de interning
TabRecon=Reconhecimento
TabWpfXaml=XAML para WPF
Unrecognised=não reconhecido
StatusReady=Abra uma pasta ou um ficheiro .baml.
StatusFolder={0}   ({1} ficheiro(s))
StatusFile={0}   {1} bytes   {2}
StatusSaved={0} guardado
StatusCopied={0} caracteres copiados
StatusCopyFailed=falha ao copiar: {0}
StatusError=erro: {0}
StatusLoadError=erro de carregamento: {0}
StatusRecords={0} registo(s)
StatusIncomplete=[INCOMPLETO]
StatusBestScore=(melhor pontuação {0}%)
DlgChooseFolder=Escolha uma pasta que contenha ficheiros .baml
DlgChooseFolderDesc=Escolha uma pasta que contenha ficheiros .baml
DlgOpenFileTitle=Abrir um ficheiro BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Todos os ficheiros (*.*)|*.*
DlgSaveXamlTitle=Guardar o XAML descompilado
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Texto (*.txt)|*.txt|Todos os ficheiros (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Este fluxo não pertence a nenhuma geração BAML suportada.
TextNoDocument=(sem documento)
TextNoRecon=(sem dados de reconhecimento para este dialeto)
TextNoRecordLayer=A camada de registos deste dialeto não está descodificada, pelo que a saída XAML não está disponível (ver Registos ou Reconhecimento).
TextXamlUnavailable=A saída XAML exige descodificação ao nível do registo, que este dialeto ainda não fornece.
TextCannotDecompile=não é possível descompilar: {0}
";

        private const string RuRu = @"
AppTitle=BamlLonghorn - декомпилятор BAML для Longhorn
MenuFile=&Файл
MenuOpenFolder=&Открыть папку...
MenuOpenFile=Открыть &файл...
MenuSaveXaml=&Сохранить XAML как...
MenuCopyView=&Копировать текущий вид
MenuExit=В&ыход
MenuView=&Вид
MenuReload=&Обновить
MenuLanguage=&Язык
MenuLanguageAuto=(как в Windows)
ColFile=файл
ColBytes=байт
ColDialect=диалект
TabSummary=Сводка
TabRecords=Записи
TabTree=Дерево
TabTables=Таблицы интернирования
TabRecon=Разведка
TabWpfXaml=XAML для WPF
Unrecognised=не распознано
StatusReady=Откройте папку или файл .baml.
StatusFolder={0}   (файлов: {1})
StatusFile={0}   {1} байт   {2}
StatusSaved=сохранено: {0}
StatusCopied=скопировано символов: {0}
StatusCopyFailed=не удалось скопировать: {0}
StatusError=ошибка: {0}
StatusLoadError=ошибка загрузки: {0}
StatusRecords=записей: {0}
StatusIncomplete=[НЕПОЛНО]
StatusBestScore=(лучший результат {0}%)
DlgChooseFolder=Выберите папку с файлами .baml
DlgChooseFolderDesc=Выберите папку с файлами .baml
DlgOpenFileTitle=Открыть файл BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Все файлы (*.*)|*.*
DlgSaveXamlTitle=Сохранить декомпилированный XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Текст (*.txt)|*.txt|Все файлы (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Этот поток не относится ни к одному поддерживаемому поколению BAML.
TextNoDocument=(нет документа)
TextNoRecon=(для этого диалекта нет данных разведки)
TextNoRecordLayer=Уровень записей этого диалекта не декодирован, поэтому вывод XAML невозможен (см. «Записи» или «Разведка»).
TextXamlUnavailable=Для вывода XAML требуется декодирование на уровне записей, которое этот диалект пока не поддерживает.
TextCannotDecompile=не удалось декомпилировать: {0}
";

        private const string PlPl = @"
AppTitle=BamlLonghorn - dekompilator BAML dla Longhorn
MenuFile=&Plik
MenuOpenFolder=&Otwórz folder...
MenuOpenFile=Otwórz &plik...
MenuSaveXaml=&Zapisz XAML jako...
MenuCopyView=&Kopiuj bieżący widok
MenuExit=Za&kończ
MenuView=&Widok
MenuReload=&Odśwież
MenuLanguage=&Język
MenuLanguageAuto=(zgodnie z Windows)
ColFile=plik
ColBytes=bajty
ColDialect=dialekt
TabSummary=Podsumowanie
TabRecords=Rekordy
TabTree=Drzewo
TabTables=Tablice internowania
TabRecon=Rozpoznanie
TabWpfXaml=XAML dla WPF
Unrecognised=nierozpoznane
StatusReady=Otwórz folder lub plik .baml.
StatusFolder={0}   (plików: {1})
StatusFile={0}   {1} bajtów   {2}
StatusSaved=zapisano {0}
StatusCopied=skopiowano {0} znaków
StatusCopyFailed=kopiowanie nie powiodło się: {0}
StatusError=błąd: {0}
StatusLoadError=błąd wczytywania: {0}
StatusRecords=rekordów: {0}
StatusIncomplete=[NIEPEŁNE]
StatusBestScore=(najlepszy wynik {0}%)
DlgChooseFolder=Wybierz folder zawierający pliki .baml
DlgChooseFolderDesc=Wybierz folder zawierający pliki .baml
DlgOpenFileTitle=Otwórz plik BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Wszystkie pliki (*.*)|*.*
DlgSaveXamlTitle=Zapisz zdekompilowany XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Tekst (*.txt)|*.txt|Wszystkie pliki (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Ten strumień nie należy do żadnej obsługiwanej generacji BAML.
TextNoDocument=(brak dokumentu)
TextNoRecon=(brak danych rozpoznania dla tego dialektu)
TextNoRecordLayer=Warstwa rekordów tego dialektu nie jest zdekodowana, więc wyjście XAML jest niedostępne (patrz Rekordy lub Rozpoznanie).
TextXamlUnavailable=Wyjście XAML wymaga dekodowania na poziomie rekordów, którego ten dialekt jeszcze nie zapewnia.
TextCannotDecompile=nie można zdekompilować: {0}
";

        private const string NlNl = @"
AppTitle=BamlLonghorn - BAML-decompiler voor Longhorn
MenuFile=&Bestand
MenuOpenFolder=Map &openen...
MenuOpenFile=&Bestand openen...
MenuSaveXaml=XAML &opslaan als...
MenuCopyView=Huidige weergave &kopiëren
MenuExit=&Afsluiten
MenuView=&Beeld
MenuReload=&Opnieuw laden
MenuLanguage=&Taal
MenuLanguageAuto=(Windows volgen)
ColFile=bestand
ColBytes=bytes
ColDialect=dialect
TabSummary=Samenvatting
TabRecords=Records
TabTree=Boom
TabTables=Interneringstabellen
TabRecon=Verkenning
TabWpfXaml=WPF-XAML
Unrecognised=onbekend
StatusReady=Open een map of een .baml-bestand.
StatusFolder={0}   ({1} bestand(en))
StatusFile={0}   {1} bytes   {2}
StatusSaved={0} opgeslagen
StatusCopied={0} tekens gekopieerd
StatusCopyFailed=kopiëren mislukt: {0}
StatusError=fout: {0}
StatusLoadError=laadfout: {0}
StatusRecords={0} record(s)
StatusIncomplete=[ONVOLLEDIG]
StatusBestScore=(beste score {0}%)
DlgChooseFolder=Kies een map met .baml-bestanden
DlgChooseFolderDesc=Kies een map met .baml-bestanden
DlgOpenFileTitle=Een BAML-bestand openen
DlgOpenFileFilter=BAML (*.baml)|*.baml|Alle bestanden (*.*)|*.*
DlgSaveXamlTitle=Gedecompileerde XAML opslaan
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Tekst (*.txt)|*.txt|Alle bestanden (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Deze stroom behoort tot geen enkele ondersteunde BAML-generatie.
TextNoDocument=(geen document)
TextNoRecon=(geen verkenningsgegevens voor dit dialect)
TextNoRecordLayer=De recordlaag van dit dialect is niet gedecodeerd, dus XAML-uitvoer is niet beschikbaar (zie Records of Verkenning).
TextXamlUnavailable=XAML-uitvoer vereist decodering op recordniveau, die dit dialect nog niet biedt.
TextCannotDecompile=kan niet decompileren: {0}
";

        private const string SvSe = @"
AppTitle=BamlLonghorn - BAML-dekompilator för Longhorn
MenuFile=&Arkiv
MenuOpenFolder=&Öppna mapp...
MenuOpenFile=Öppna &fil...
MenuSaveXaml=&Spara XAML som...
MenuCopyView=&Kopiera aktuell vy
MenuExit=&Avsluta
MenuView=&Visa
MenuReload=&Läs in igen
MenuLanguage=&Språk
MenuLanguageAuto=(följ Windows)
ColFile=fil
ColBytes=byte
ColDialect=dialekt
TabSummary=Sammanfattning
TabRecords=Poster
TabTree=Träd
TabTables=Interningstabeller
TabRecon=Spaning
TabWpfXaml=WPF-XAML
Unrecognised=okänd
StatusReady=Öppna en mapp eller en .baml-fil.
StatusFolder={0}   ({1} fil(er))
StatusFile={0}   {1} byte   {2}
StatusSaved={0} sparad
StatusCopied={0} tecken kopierade
StatusCopyFailed=kopiering misslyckades: {0}
StatusError=fel: {0}
StatusLoadError=inläsningsfel: {0}
StatusRecords={0} post(er)
StatusIncomplete=[OFRÅNSTÄNDIG]
StatusBestScore=(bästa poäng {0}%)
DlgChooseFolder=Välj en mapp som innehåller .baml-filer
DlgChooseFolderDesc=Välj en mapp som innehåller .baml-filer
DlgOpenFileTitle=Öppna en BAML-fil
DlgOpenFileFilter=BAML (*.baml)|*.baml|Alla filer (*.*)|*.*
DlgSaveXamlTitle=Spara dekompilerad XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Text (*.txt)|*.txt|Alla filer (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Den här strömmen tillhör ingen BAML-generation som stöds.
TextNoDocument=(inget dokument)
TextNoRecon=(inga spanningsdata för denna dialekt)
TextNoRecordLayer=Denna dialekts postlager är inte avkodat, så XAML-utdata är inte tillgänglig (se Poster eller Spaning).
TextXamlUnavailable=XAML-utdata kräver avkodning på postnivå, vilket denna dialekt ännu inte erbjuder.
TextCannotDecompile=kan inte dekompilera: {0}
";

        private const string TrTr = @"
AppTitle=BamlLonghorn - Longhorn BAML derleyici çözücü
MenuFile=&Dosya
MenuOpenFolder=&Klasör aç...
MenuOpenFile=&Dosya aç...
MenuSaveXaml=XAML &olarak kaydet...
MenuCopyView=Geçerli &görünümü kopyala
MenuExit=Ç&ıkış
MenuView=&Görünüm
MenuReload=&Yeniden yükle
MenuLanguage=&Dil
MenuLanguageAuto=(Windows'u izle)
ColFile=dosya
ColBytes=bayt
ColDialect=lehçe
TabSummary=Özet
TabRecords=Kayıtlar
TabTree=Ağaç
TabTables=İç içe tablolar
TabRecon=Keşif
TabWpfXaml=WPF XAML
Unrecognised=tanınmadı
StatusReady=Bir klasör veya .baml dosyası açın.
StatusFolder={0}   ({1} dosya)
StatusFile={0}   {1} bayt   {2}
StatusSaved={0} kaydedildi
StatusCopied={0} karakter kopyalandı
StatusCopyFailed=kopyalama başarısız: {0}
StatusError=hata: {0}
StatusLoadError=yükleme hatası: {0}
StatusRecords={0} kayıt
StatusIncomplete=[EKSİK]
StatusBestScore=(en iyi puan {0}%)
DlgChooseFolder=.baml dosyaları içeren bir klasör seçin
DlgChooseFolderDesc=.baml dosyaları içeren bir klasör seçin
DlgOpenFileTitle=BAML dosyası aç
DlgOpenFileFilter=BAML (*.baml)|*.baml|Tüm dosyalar (*.*)|*.*
DlgSaveXamlTitle=Çözülen XAML'i kaydet
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Metin (*.txt)|*.txt|Tüm dosyalar (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Bu akış desteklenen bir BAML nesline ait değil.
TextNoDocument=(belge yok)
TextNoRecon=(bu lehçe için keşif verisi yok)
TextNoRecordLayer=Bu lehçenin kayıt katmanı çözülmediğinden XAML çıktısı kullanılamaz (Kayıtlar veya Keşif'e bakın).
TextXamlUnavailable=XAML çıktısı kayıt düzeyinde çözme gerektirir; bu lehçe bunu henüz sağlamıyor.
TextCannotDecompile=çözülemedi: {0}
";

        private const string CsCz = @"
AppTitle=BamlLonghorn - dekompilátor BAML pro Longhorn
MenuFile=&Soubor
MenuOpenFolder=&Otevřít složku...
MenuOpenFile=Otevřít &soubor...
MenuSaveXaml=&Uložit XAML jako...
MenuCopyView=&Kopírovat aktuální zobrazení
MenuExit=U&končit
MenuView=&Zobrazení
MenuReload=&Znovu načíst
MenuLanguage=&Jazyk
MenuLanguageAuto=(podle Windows)
ColFile=soubor
ColBytes=bajty
ColDialect=dialekt
TabSummary=Souhrn
TabRecords=Záznamy
TabTree=Strom
TabTables=Interní tabulky
TabRecon=Průzkum
TabWpfXaml=WPF XAML
Unrecognised=nerozpoznáno
StatusReady=Otevřete složku nebo soubor .baml.
StatusFolder={0}   (souborů: {1})
StatusFile={0}   {1} bajtů   {2}
StatusSaved=uloženo {0}
StatusCopied=zkopírováno {0} znaků
StatusCopyFailed=kopírování se nezdařilo: {0}
StatusError=chyba: {0}
StatusLoadError=chyba načítání: {0}
StatusRecords=záznamů: {0}
StatusIncomplete=[NEEPLNÉ]
StatusBestScore=(nejlepší skóre {0}%)
DlgChooseFolder=Zvolte složku obsahující soubory .baml
DlgChooseFolderDesc=Zvolte složku obsahující soubory .baml
DlgOpenFileTitle=Otevřít soubor BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Všechny soubory (*.*)|*.*
DlgSaveXamlTitle=Uložit dekompilovaný XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Text (*.txt)|*.txt|Všechny soubory (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Tento proud nepatří žádné podporované generaci BAML.
TextNoDocument=(žádný dokument)
TextNoRecon=(pro tento dialekt nejsou data průzkumu)
TextNoRecordLayer=Vrstva záznamů tohoto dialektu není dekódována, proto není výstup XAML k dispozici (viz Záznamy nebo Průzkum).
TextXamlUnavailable=Výstup XAML vyžaduje dekódování na úrovni záznamů, které tento dialekt zatím neposkytuje.
TextCannotDecompile=nelze dekompilovat: {0}
";

        private const string HuHu = @"
AppTitle=BamlLonghorn - Longhorn BAML visszafejtő
MenuFile=&Fájl
MenuOpenFolder=Mappa &megnyitása...
MenuOpenFile=&Fájl megnyitása...
MenuSaveXaml=XAML &mentése másként...
MenuCopyView=Jelenlegi nézet &másolása
MenuExit=K&ilépés
MenuView=&Nézet
MenuReload=&Újratöltés
MenuLanguage=&Nyelv
MenuLanguageAuto=(Windows követése)
ColFile=fájl
ColBytes=bájt
ColDialect=dialektus
TabSummary=Összegzés
TabRecords=Rekordok
TabTree=Fa
TabTables=Internálási táblák
TabRecon=Felderítés
TabWpfXaml=WPF XAML
Unrecognised=ismeretlen
StatusReady=Nyisson meg egy mappát vagy .baml fájlt.
StatusFolder={0}   ({1} fájl)
StatusFile={0}   {1} bájt   {2}
StatusSaved={0} mentve
StatusCopied={0} karakter másolva
StatusCopyFailed=a másolás nem sikerült: {0}
StatusError=hiba: {0}
StatusLoadError=betöltési hiba: {0}
StatusRecords={0} rekord
StatusIncomplete=[HIÁNYOS]
StatusBestScore=(legjobb pontszám {0}%)
DlgChooseFolder=Válasszon .baml fájlokat tartalmazó mappát
DlgChooseFolderDesc=Válasszon .baml fájlokat tartalmazó mappát
DlgOpenFileTitle=BAML fájl megnyitása
DlgOpenFileFilter=BAML (*.baml)|*.baml|Minden fájl (*.*)|*.*
DlgSaveXamlTitle=A visszafejtett XAML mentése
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Szöveg (*.txt)|*.txt|Minden fájl (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Ez a stream egyetlen támogatott BAML-generációhoz sem tartozik.
TextNoDocument=(nincs dokumentum)
TextNoRecon=(nincs felderítési adat ehhez a dialektushoz)
TextNoRecordLayer=Ennek a dialektusnak a rekordrétege nincs dekódolva, ezért a XAML kimenet nem érhető el (lásd Rekordok vagy Felderítés).
TextXamlUnavailable=A XAML kimenethez rekordszintű dekódolás kell, amelyet ez a dialektus még nem nyújt.
TextCannotDecompile=nem sikerült visszafejteni: {0}
";

        private const string ArSa = @"
AppTitle=BamlLonghorn - مفكك BAML الخاص بـ Longhorn
MenuFile=ملف(&F)
MenuOpenFolder=فتح مجلد(&O)...
MenuOpenFile=فتح ملف(&F)...
MenuSaveXaml=حفظ XAML باسم(&S)...
MenuCopyView=نسخ العرض الحالي(&C)
MenuExit=خروج(&X)
MenuView=عرض(&V)
MenuReload=إعادة تحميل(&R)
MenuLanguage=اللغة(&L)
MenuLanguageAuto=(اتباع Windows)
ColFile=الملف
ColBytes=بايت
ColDialect=اللهجة
TabSummary=ملخص
TabRecords=السجلات
TabTree=شجرة
TabTables=جداول التجميع
TabRecon=استطلاع
TabWpfXaml=XAML لـ WPF
Unrecognised=غير معروف
StatusReady=افتح مجلدًا أو ملف ‎.baml.
StatusFolder={0}   ({1} ملف)
StatusFile={0}   {1} بايت   {2}
StatusSaved=تم حفظ {0}
StatusCopied=تم نسخ {0} حرفًا
StatusCopyFailed=فشل النسخ: {0}
StatusError=خطأ: {0}
StatusLoadError=خطأ في التحميل: {0}
StatusRecords={0} سجل
StatusIncomplete=[غير مكتمل]
StatusBestScore=(أفضل نتيجة {0}%)
DlgChooseFolder=اختر مجلدًا يحتوي على ملفات ‎.baml
DlgChooseFolderDesc=اختر مجلدًا يحتوي على ملفات ‎.baml
DlgOpenFileTitle=فتح ملف BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|كل الملفات (*.*)|*.*
DlgSaveXamlTitle=حفظ XAML المفكك
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|نص (*.txt)|*.txt|كل الملفات (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=هذا التدفق لا ينتمي إلى أي جيل BAML مدعوم.
TextNoDocument=(لا مستند)
TextNoRecon=(لا توجد بيانات استطلاع لهذه اللهجة)
TextNoRecordLayer=طبقة سجلات هذه اللهجة غير مفككة، لذا لا يتوفر إخراج XAML (انظر السجلات أو الاستطلاع).
TextXamlUnavailable=يتطلب إخراج XAML فكًا على مستوى السجل، وهو ما لا توفره هذه اللهجة بعد.
TextCannotDecompile=تعذر التفكيك: {0}
";

        private const string HeIl = @"
AppTitle=BamlLonghorn - מהדר-פוך BAML של Longhorn
MenuFile=&קובץ
MenuOpenFolder=&פתח תיקייה...
MenuOpenFile=פתח &קובץ...
MenuSaveXaml=&שמור XAML בשם...
MenuCopyView=&העתק תצוגה נוכחית
MenuExit=&יציאה
MenuView=&תצוגה
MenuReload=&טען מחדש
MenuLanguage=&שפה
MenuLanguageAuto=(לפי Windows)
ColFile=קובץ
ColBytes=בתים
ColDialect=ניב
TabSummary=סיכום
TabRecords=רשומות
TabTree=עץ
TabTables=טבלאות אחסון
TabRecon=סיור
TabWpfXaml=XAML של WPF
Unrecognised=לא מזוהה
StatusReady=פתח תיקייה או קובץ ‎.baml.
StatusFolder={0}   ({1} קבצים)
StatusFile={0}   {1} בתים   {2}
StatusSaved=נשמר {0}
StatusCopied=הועתקו {0} תווים
StatusCopyFailed=ההעתקה נכשלה: {0}
StatusError=שגיאה: {0}
StatusLoadError=שגיאת טעינה: {0}
StatusRecords={0} רשומות
StatusIncomplete=[לא שלם]
StatusBestScore=(ניקוד מיטבי {0}%)
DlgChooseFolder=בחר תיקייה המכילה קבצי ‎.baml
DlgChooseFolderDesc=בחר תיקייה המכילה קבצי ‎.baml
DlgOpenFileTitle=פתח קובץ BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|כל הקבצים (*.*)|*.*
DlgSaveXamlTitle=שמור XAML מפוענח
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|טקסט (*.txt)|*.txt|כל הקבצים (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=זרם זה אינו שייך לאף דור BAML נתמך.
TextNoDocument=(אין מסמך)
TextNoRecon=(אין נתוני סיור לניב זה)
TextNoRecordLayer=שכבת הרשומות של ניב זה אינה מפוענחת, לכן פלט XAML אינו זמין (ראה רשומות או סיור).
TextXamlUnavailable=פלט XAML דורש פענוח ברמת הרשומה, שניב זה עדיין אינו מספק.
TextCannotDecompile=לא ניתן לפענח: {0}
";

        private const string ThTh = @"
AppTitle=BamlLonghorn - ตัวถอดรหัส BAML ของ Longhorn
MenuFile=ไฟล์(&F)
MenuOpenFolder=เปิดโฟลเดอร์(&O)...
MenuOpenFile=เปิดไฟล์(&F)...
MenuSaveXaml=บันทึก XAML เป็น(&S)...
MenuCopyView=คัดลอกมุมมองปัจจุบัน(&C)
MenuExit=ออก(&X)
MenuView=มุมมอง(&V)
MenuReload=โหลดใหม่(&R)
MenuLanguage=ภาษา(&L)
MenuLanguageAuto=(ตาม Windows)
ColFile=ไฟล์
ColBytes=ไบต์
ColDialect=ภาษาถิ่น
TabSummary=สรุป
TabRecords=เรกคอร์ด
TabTree=ทรี
TabTables=ตารางอินเทิร์น
TabRecon=การสำรวจ
TabWpfXaml=WPF XAML
Unrecognised=ไม่รู้จัก
StatusReady=เปิดโฟลเดอร์หรือไฟล์ .baml
StatusFolder={0}   ({1} ไฟล์)
StatusFile={0}   {1} ไบต์   {2}
StatusSaved=บันทึก {0} แล้ว
StatusCopied=คัดลอก {0} ตัวอักษรแล้ว
StatusCopyFailed=คัดลอกไม่สำเร็จ: {0}
StatusError=ข้อผิดพลาด: {0}
StatusLoadError=ข้อผิดพลาดในการโหลด: {0}
StatusRecords={0} เรกคอร์ด
StatusIncomplete=[ไม่สมบูรณ์]
StatusBestScore=(คะแนนสูงสุด {0}%)
DlgChooseFolder=เลือกโฟลเดอร์ที่มีไฟล์ .baml
DlgChooseFolderDesc=เลือกโฟลเดอร์ที่มีไฟล์ .baml
DlgOpenFileTitle=เปิดไฟล์ BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|ไฟล์ทั้งหมด (*.*)|*.*
DlgSaveXamlTitle=บันทึก XAML ที่ถอดรหัสแล้ว
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|ข้อความ (*.txt)|*.txt|ไฟล์ทั้งหมด (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=สตรีมนี้ไม่ใช่รุ่น BAML ที่รองรับ
TextNoDocument=(ไม่มีเอกสาร)
TextNoRecon=(ไม่มีข้อมูลการสำรวจสำหรับภาษาถิ่นนี้)
TextNoRecordLayer=ชั้นเรกคอร์ดของภาษาถิ่นนี้ยังไม่ถูกถอดรหัส จึงไม่มีเอาต์พุต XAML (ดู เรกคอร์ด หรือ การสำรวจ)
TextXamlUnavailable=เอาต์พุต XAML ต้องถอดรหัสระดับเรกคอร์ด ซึ่งภาษาถิ่นนี้ยังไม่รองรับ
TextCannotDecompile=ถอดรหัสไม่ได้: {0}
";

        private const string ViVn = @"
AppTitle=BamlLonghorn - trình dịch ngược BAML của Longhorn
MenuFile=&Tệp
MenuOpenFolder=&Mở thư mục...
MenuOpenFile=Mở &tệp...
MenuSaveXaml=&Lưu XAML thành...
MenuCopyView=&Sao chép chế độ xem hiện tại
MenuExit=Th&oát
MenuView=&Xem
MenuReload=&Tải lại
MenuLanguage=&Ngôn ngữ
MenuLanguageAuto=(theo Windows)
ColFile=tệp
ColBytes=byte
ColDialect=phương ngữ
TabSummary=Tóm tắt
TabRecords=Bản ghi
TabTree=Cây
TabTables=Bảng nội bộ
TabRecon=Trinh sát
TabWpfXaml=WPF XAML
Unrecognised=không nhận dạng
StatusReady=Hãy mở một thư mục hoặc tệp .baml.
StatusFolder={0}   ({1} tệp)
StatusFile={0}   {1} byte   {2}
StatusSaved=đã lưu {0}
StatusCopied=đã sao chép {0} ký tự
StatusCopyFailed=sao chép thất bại: {0}
StatusError=lỗi: {0}
StatusLoadError=lỗi tải: {0}
StatusRecords={0} bản ghi
StatusIncomplete=[CHƯA ĐẦY ĐỦ]
StatusBestScore=(điểm cao nhất {0}%)
DlgChooseFolder=Chọn thư mục chứa tệp .baml
DlgChooseFolderDesc=Chọn thư mục chứa tệp .baml
DlgOpenFileTitle=Mở tệp BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Tất cả tệp (*.*)|*.*
DlgSaveXamlTitle=Lưu XAML đã dịch ngược
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Văn bản (*.txt)|*.txt|Tất cả tệp (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Luồng này không thuộc thế hệ BAML nào được hỗ trợ.
TextNoDocument=(không có tài liệu)
TextNoRecon=(không có dữ liệu trinh sát cho phương ngữ này)
TextNoRecordLayer=Lớp bản ghi của phương ngữ này chưa được giải mã nên không có đầu ra XAML (xem Bản ghi hoặc Trinh sát).
TextXamlUnavailable=Đầu ra XAML yêu cầu giải mã ở cấp bản ghi, điều mà phương ngữ này chưa hỗ trợ.
TextCannotDecompile=không thể dịch ngược: {0}
";

        private const string IdId = @"
AppTitle=BamlLonghorn - dekompiler BAML Longhorn
MenuFile=&Berkas
MenuOpenFolder=&Buka folder...
MenuOpenFile=Buka &berkas...
MenuSaveXaml=&Simpan XAML sebagai...
MenuCopyView=&Salin tampilan saat ini
MenuExit=&Keluar
MenuView=&Tampilan
MenuReload=&Muat ulang
MenuLanguage=&Bahasa
MenuLanguageAuto=(ikuti Windows)
ColFile=berkas
ColBytes=bita
ColDialect=dialek
TabSummary=Ringkasan
TabRecords=Rekaman
TabTree=Pohon
TabTables=Tabel internal
TabRecon=Pengintaian
TabWpfXaml=WPF XAML
Unrecognised=tidak dikenali
StatusReady=Buka folder atau berkas .baml.
StatusFolder={0}   ({1} berkas)
StatusFile={0}   {1} bita   {2}
StatusSaved={0} disimpan
StatusCopied={0} karakter disalin
StatusCopyFailed=gagal menyalin: {0}
StatusError=kesalahan: {0}
StatusLoadError=kesalahan pemuatan: {0}
StatusRecords={0} rekaman
StatusIncomplete=[TIDAK LENGKAP]
StatusBestScore=(skor terbaik {0}%)
DlgChooseFolder=Pilih folder yang berisi berkas .baml
DlgChooseFolderDesc=Pilih folder yang berisi berkas .baml
DlgOpenFileTitle=Buka berkas BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Semua berkas (*.*)|*.*
DlgSaveXamlTitle=Simpan XAML hasil dekompilasi
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Teks (*.txt)|*.txt|Semua berkas (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Aliran ini bukan bagian dari generasi BAML yang didukung.
TextNoDocument=(tidak ada dokumen)
TextNoRecon=(tidak ada data pengintaian untuk dialek ini)
TextNoRecordLayer=Lapisan rekaman dialek ini belum didekode, jadi keluaran XAML tidak tersedia (lihat Rekaman atau Pengintaian).
TextXamlUnavailable=Keluaran XAML memerlukan dekode tingkat rekaman, yang belum disediakan dialek ini.
TextCannotDecompile=tidak dapat mendekompilasi: {0}
";

        private const string UkUa = @"
AppTitle=BamlLonghorn - декомпілятор BAML для Longhorn
MenuFile=&Файл
MenuOpenFolder=&Відкрити теку...
MenuOpenFile=Відкрити &файл...
MenuSaveXaml=&Зберегти XAML як...
MenuCopyView=&Копіювати поточний вигляд
MenuExit=В&ихід
MenuView=&Вигляд
MenuReload=&Оновити
MenuLanguage=&Мова
MenuLanguageAuto=(як у Windows)
ColFile=файл
ColBytes=байт
ColDialect=діалект
TabSummary=Підсумок
TabRecords=Записи
TabTree=Дерево
TabTables=Таблиці інтернування
TabRecon=Розвідка
TabWpfXaml=XAML для WPF
Unrecognised=не розпізнано
StatusReady=Відкрийте теку або файл .baml.
StatusFolder={0}   (файлів: {1})
StatusFile={0}   {1} байт   {2}
StatusSaved=збережено {0}
StatusCopied=скопійовано символів: {0}
StatusCopyFailed=не вдалося скопіювати: {0}
StatusError=помилка: {0}
StatusLoadError=помилка завантаження: {0}
StatusRecords=записів: {0}
StatusIncomplete=[НЕПОВНИЙ]
StatusBestScore=(найкращий результат {0}%)
DlgChooseFolder=Виберіть теку з файлами .baml
DlgChooseFolderDesc=Виберіть теку з файлами .baml
DlgOpenFileTitle=Відкрити файл BAML
DlgOpenFileFilter=BAML (*.baml)|*.baml|Усі файли (*.*)|*.*
DlgSaveXamlTitle=Зберегти декомпільований XAML
DlgSaveXamlFilter=XAML (*.xaml)|*.xaml|Текст (*.txt)|*.txt|Усі файли (*.*)|*.*
DlgErrorTitle=BamlLonghorn
TextUnrecognised=Цей потік не належить до жодного підтримуваного покоління BAML.
TextNoDocument=(немає документа)
TextNoRecon=(немає даних розвідки для цього діалекту)
TextNoRecordLayer=Рівень записів цього діалекту не декодовано, тому вивід XAML недоступний (див. «Записи» або «Розвідка»).
TextXamlUnavailable=Для виводу XAML потрібне декодування на рівні записів, яке цей діалект ще не підтримує.
TextCannotDecompile=не вдалося декомпілювати: {0}
";
    }
}
