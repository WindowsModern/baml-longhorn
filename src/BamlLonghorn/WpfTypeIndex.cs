using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// Knows which type names exist in the WPF assemblies installed on this machine.
    ///
    /// This is what makes the converter's "can this be translated" answer true for the machine it
    /// runs on rather than for the machine it was written on. Longhorn names a type; if WPF has a
    /// type of that name, it survives the conversion, and if it does not, it is marked.
    ///
    /// The index is built by reflecting over the presentation assemblies and is cached, because
    /// building it costs a few hundred milliseconds and a conversion may run over hundreds of
    /// files. When reflection is unavailable -- a trimmed or single-file host, or an unexpected
    /// framework layout -- the index falls back to a name list compiled in from a real WPF
    /// 4.0 decompile, and <see cref="Source"/> says which source was used so a caller can report it.
    /// </summary>
    public static class WpfTypeIndex
    {
        private static readonly object _lock = new object();
        private static HashSet<string> _fullNames;
        private static HashSet<string> _shortNames;
        private static string _source = "(not built)";
        private static string[] _assembliesProbed = new string[0];

        /// <summary>Where the index came from: "reflection" or "fallback list".</summary>
        public static string Source
        {
            get { EnsureBuilt(); return _source; }
        }

        /// <summary>The assembly file names the index was built from.</summary>
        public static string[] AssembliesProbed
        {
            get { EnsureBuilt(); return _assembliesProbed; }
        }

        public static int TypeCount
        {
            get { EnsureBuilt(); return _fullNames.Count; }
        }

        /// <summary>
        /// True when WPF defines a type of this name. A fully qualified name is matched exactly;
        /// a bare name is matched against any namespace, because that is how markup resolves it.
        /// </summary>
        public static bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            EnsureBuilt();

            if (name.IndexOf('.') >= 0)
            {
                return _fullNames.Contains(name) || _shortNames.Contains(name);
            }
            // strip a prefix the converter itself may have added
            int colon = name.IndexOf(':');
            if (colon >= 0) name = name.Substring(colon + 1);
            return _shortNames.Contains(name);
        }

        private static void EnsureBuilt()
        {
            if (_fullNames != null) return;
            lock (_lock)
            {
                if (_fullNames != null) return;
                Build();
            }
        }

        /// <summary>
        /// Finds an assembly inside the .NET Framework global assembly cache.
        ///
        /// Every GAC root is searched rather than one, because the presentation assemblies are not
        /// all in the same place: PresentationCore sits under GAC_32 on machines where the others
        /// are under GAC_MSIL. A GAC layout is
        /// <c>&lt;root&gt;\&lt;AssemblyName&gt;\&lt;version__key&gt;\&lt;AssemblyName&gt;.dll</c>,
        /// so the version directory is enumerated and the newest is preferred.
        ///
        /// Returns null when nothing matches, which the caller treats as "this assembly is not
        /// available" rather than as an error.
        /// </summary>
        private static string FindInGac(string simpleName)
        {
            string windows = null;
            try { windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows); }
            catch (Exception) { }
            if (string.IsNullOrEmpty(windows)) return null;

            string assemblyRoot = Path.Combine(windows, "Microsoft.NET", "assembly");
            if (!Directory.Exists(assemblyRoot)) return null;

            string[] roots;
            try { roots = Directory.GetDirectories(assemblyRoot); }
            catch (Exception) { return null; }

            string best = null;
            string bestVersion = null;

            foreach (string root in roots)
            {
                string container = Path.Combine(root, simpleName);
                if (!Directory.Exists(container)) continue;

                string[] versions;
                try { versions = Directory.GetDirectories(container); }
                catch (Exception) { continue; }

                foreach (string vdir in versions)
                {
                    string dll = Path.Combine(vdir, simpleName + ".dll");
                    if (!File.Exists(dll)) continue;

                    // the directory name starts with the version, e.g. v4.0_4.0.0.0__31bf...
                    string version = Path.GetFileName(vdir);
                    if (best == null || string.CompareOrdinal(version, bestVersion) > 0)
                    {
                        best = dll;
                        bestVersion = version;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Builds the index, preferring reflection and falling back to the generated list.
        /// </summary>
        private static void Build()
        {
            var full = new HashSet<string>(StringComparer.Ordinal);
            var shortNames = new HashSet<string>(StringComparer.Ordinal);
            var probed = new List<string>();

            // Locating the presentation assemblies is the whole difficulty here, and getting it
            // wrong is silent: a failed probe just means every WPF type looks unavailable, so the
            // converter marks everything. Two facts make the naive approaches fail:
            //
            //   * the assemblies are not beside mscorlib. On .NET Framework they are in the GAC;
            //     on .NET Core they are in the shared WindowsDesktop framework.
            //   * they are not all in the same GAC root. On this machine PresentationFramework and
            //     WindowsBase are under GAC_MSIL while PresentationCore is under GAC_32, so a
            //     single hard-coded directory finds some and misses others.
            //
            // So each is resolved by simple name first and, failing that, by searching every GAC
            // root and the runtime directory for a directory whose name matches the assembly.
            string[] wanted = new string[]
            {
                "PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml"
            };

            string baseDir = null;
            try { baseDir = Path.GetDirectoryName(typeof(object).Assembly.Location); }
            catch (Exception) { }

            foreach (string simple in wanted)
            {
                Assembly asm = null;

                // 1. by simple name: resolves from the GAC, and from the shared framework on
                //    .NET Core
                try { asm = Assembly.Load(simple); }
                catch (Exception) { }

                // 2. a file in the runtime directory
                if (asm == null && baseDir != null)
                {
                    string path = Path.Combine(baseDir, simple + ".dll");
                    if (File.Exists(path))
                    {
                        try { asm = Assembly.LoadFrom(path); }
                        catch (Exception) { }
                    }
                }

                // 3. any GAC root containing a version directory for this assembly
                if (asm == null)
                {
                    string found = FindInGac(simple);
                    if (found != null)
                    {
                        try { asm = Assembly.LoadFrom(found); }
                        catch (Exception) { }
                    }
                }

                if (asm == null) continue;

                try
                {
                    Type[] types;
                    try { types = asm.GetExportedTypes(); }
                    catch (ReflectionTypeLoadException ex)
                    {
                        var list = new List<Type>();
                        foreach (Type t in ex.Types) if (t != null) list.Add(t);
                        types = list.ToArray();
                    }
                    foreach (Type t in types)
                    {
                        if (t.FullName != null) full.Add(t.FullName);
                        shortNames.Add(t.Name);
                    }
                    probed.Add(simple + ".dll");
                }
                catch (Exception)
                {
                    // one assembly failing must not lose the others
                }
            }

            if (full.Count > 0)
            {
                _source = "reflection";
            }
            else
            {
                foreach (string n in FallbackNames.ShortNames) shortNames.Add(n);
                foreach (string n in FallbackNames.FullNames) full.Add(n);
                _source = "fallback list";
            }

            _assembliesProbed = probed.ToArray();
            _shortNames = shortNames;
            _fullNames = full;
        }
    }
}
