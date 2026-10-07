// Answers, for every element type the corpora contain, whether a type of that name exists in
// the WPF assemblies actually installed on this machine.
//
// This is the mechanism behind the converter's "can this be translated" decision. The
// alternative -- a hand-written table of what WPF has -- would go stale and would be wrong about
// the machine it runs on. Querying the loaded assemblies means the answer is always the truth
// for the installed framework, which is exactly what the request asked for: convert according to
// what the machine supports, and mark what cannot be converted.
//
// Usage:  resolve-types <assemblies...>
//         reads fully qualified type names on stdin, writes "OK <name>" or "NO <name>"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("usage: resolve-types <assembly>..."); return 2; }

        var known = new HashSet<string>(StringComparer.Ordinal);
        var byShortName = new HashSet<string>(StringComparer.Ordinal);
        int assemblies = 0;

        foreach (var path in args)
        {
            Assembly asm;
            try { asm = Assembly.LoadFrom(path); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("cannot load " + path + ": " + ex.GetType().Name);
                continue;
            }
            assemblies++;
            Type[] types;
            try { types = asm.GetExportedTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types)
            {
                if (t.FullName != null) known.Add(t.FullName);
                byShortName.Add(t.Name);
            }
        }

        Console.Error.WriteLine("assemblies loaded: " + assemblies);
        Console.Error.WriteLine("public types indexed: " + known.Count);

        string line;
        while ((line = Console.ReadLine()) != null)
        {
            string name = line.Trim();
            if (name.Length == 0) continue;

            // a name may arrive fully qualified, or as a bare type name from markup
            bool ok = known.Contains(name) || byShortName.Contains(name);
            // WPF also resolves a bare name against every xmlns-imported namespace, so a bare
            // name is accepted when any namespace in the indexed set ends with it
            if (!ok && !name.Contains('.'))
                ok = known.Any(f => f.EndsWith("." + name, StringComparison.Ordinal));

            Console.WriteLine((ok ? "OK\t" : "NO\t") + name);
        }
        return 0;
    }
}
