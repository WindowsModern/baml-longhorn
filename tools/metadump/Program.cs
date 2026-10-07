using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace MetaDump
{
    /// <summary>
    /// Metadata-only reader for the Longhorn assemblies.
    ///
    /// Purpose: recover the BAML format definition (BamlTag, BamlRecordType,
    /// Baml*Record field layouts) from the compiled assembly WITHOUT needing a
    /// decompiler. System.Reflection.Metadata reads the tables directly, so
    /// pre-release .NET 2.0 assemblies load fine -- no type resolution, no
    /// dependency loading.
    ///
    /// Usage:
    ///   metadump &lt;assembly&gt; types [filter]     list type names
    ///   metadump &lt;assembly&gt; type  &lt;fullName&gt;   dump one type: kind, base,
    ///                                           fields (with offsets), methods
    ///   metadump &lt;assembly&gt; enum  &lt;fullName&gt;   dump enum name=value pairs
    ///   metadump &lt;assembly&gt; search &lt;regex&gt;     types matching a regex
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine(
                    "usage: metadump <assembly> types|search|type|enum [arg]");
                return 1;
            }

            string path = args[0];
            string command = args[1].ToLowerInvariant();
            string arg = args.Length > 2 ? args[2] : null;

            if (!File.Exists(path))
            {
                Console.Error.WriteLine("not found: " + path);
                return 2;
            }

            using (FileStream fs = File.OpenRead(path))
            using (PEReader pe = new PEReader(fs))
            {
                if (!pe.HasMetadata)
                {
                    Console.Error.WriteLine("no managed metadata in " + path);
                    return 3;
                }
                MetadataReader md = pe.GetMetadataReader();

                switch (command)
                {
                    case "types":
                        return ListTypes(md, arg);
                    case "search":
                        return SearchTypes(md, arg);
                    case "type":
                        return DumpType(md, arg);
                    case "enum":
                        return DumpEnum(md, arg);
                    default:
                        Console.Error.WriteLine("unknown command: " + command);
                        return 1;
                }
            }
        }

        private static string FullName(MetadataReader md, TypeDefinition td)
        {
            string ns = md.GetString(td.Namespace);
            string name = md.GetString(td.Name);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static IEnumerable<TypeDefinition> AllTypes(MetadataReader md)
        {
            foreach (TypeDefinitionHandle h in md.TypeDefinitions)
            {
                yield return md.GetTypeDefinition(h);
            }
        }

        private static int ListTypes(MetadataReader md, string filter)
        {
            foreach (TypeDefinition td in AllTypes(md)
                         .OrderBy(t => FullName(md, t), StringComparer.Ordinal))
            {
                string fn = FullName(md, td);
                if (filter == null ||
                    fn.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine(fn);
                }
            }
            return 0;
        }

        private static int SearchTypes(MetadataReader md, string pattern)
        {
            if (pattern == null)
            {
                Console.Error.WriteLine("search needs a regex");
                return 1;
            }
            var re = new System.Text.RegularExpressions.Regex(
                pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (TypeDefinition td in AllTypes(md)
                         .OrderBy(t => FullName(md, t), StringComparer.Ordinal))
            {
                string fn = FullName(md, td);
                if (re.IsMatch(fn))
                {
                    Console.WriteLine(fn);
                }
            }
            return 0;
        }

        private static TypeDefinition? Find(MetadataReader md, string fullName)
        {
            foreach (TypeDefinition td in AllTypes(md))
            {
                if (string.Equals(FullName(md, td), fullName,
                                  StringComparison.Ordinal))
                {
                    return td;
                }
            }
            // fall back to suffix match
            foreach (TypeDefinition td in AllTypes(md))
            {
                if (FullName(md, td).EndsWith("." + fullName, StringComparison.Ordinal))
                {
                    return td;
                }
            }
            return null;
        }

        private static bool IsEnum(MetadataReader md, TypeDefinition td)
        {
            if (td.BaseType.IsNil)
            {
                return false;
            }
            EntityHandle baseHandle = td.BaseType;
            if (baseHandle.Kind != HandleKind.TypeReference)
            {
                return false;
            }
            TypeReference tr = md.GetTypeReference((TypeReferenceHandle)baseHandle);
            return md.GetString(tr.Name) == "Enum";
        }

        private static int DumpType(MetadataReader md, string fullName)
        {
            TypeDefinition? found = Find(md, fullName);
            if (found == null)
            {
                Console.Error.WriteLine("type not found: " + fullName);
                return 4;
            }
            TypeDefinition td = found.Value;
            Console.WriteLine("type   : " + FullName(md, td));

            if (!td.BaseType.IsNil)
            {
                Console.WriteLine("base   : " + DescribeHandle(md, td.BaseType));
            }
            Console.WriteLine("attrs  : " + td.Attributes);
            Console.WriteLine("nested : " + (td.IsNested ? "yes" : "no"));

            Console.WriteLine();
            Console.WriteLine("fields (" + td.GetFields().Count + "):");
            foreach (FieldDefinitionHandle fh in td.GetFields())
            {
                FieldDefinition fd = md.GetFieldDefinition(fh);
                string name = md.GetString(fd.Name);
                string type = DescribeSignature(md, fd.DecodeSignature(
                    new SigProvider(), null));
                int offset = -1;
                try
                {
                    offset = fd.GetOffset();
                }
                catch (BadImageFormatException)
                {
                }
                Console.WriteLine("  " + type.PadRight(40) + " " + name
                                  + (offset >= 0 ? "   @+" + offset : ""));
            }

            Console.WriteLine();
            Console.WriteLine("methods:");
            foreach (MethodDefinitionHandle mh in td.GetMethods())
            {
                MethodDefinition m = md.GetMethodDefinition(mh);
                string name = md.GetString(m.Name);
                string ret = DescribeSignature(md, m.DecodeSignature(
                    new SigProvider(), null));
                Console.WriteLine("  " + ret.PadRight(24) + " " + name
                                  + "  (" + m.Attributes + ")");
            }
            return 0;
        }

        private static int DumpEnum(MetadataReader md, string fullName)
        {
            TypeDefinition? found = Find(md, fullName);
            if (found == null)
            {
                Console.Error.WriteLine("type not found: " + fullName);
                return 4;
            }
            TypeDefinition td = found.Value;
            Console.WriteLine("enum " + FullName(md, td)
                              + (IsEnum(md, td) ? "" : "  (NOT an enum!)"));
            foreach (FieldDefinitionHandle fh in td.GetFields())
            {
                FieldDefinition fd = md.GetFieldDefinition(fh);
                string name = md.GetString(fd.Name);
                if (name == "value__")
                {
                    continue;
                }
                ConstantHandle ch = fd.GetDefaultValue();
                string value = "?";
                if (!ch.IsNil)
                {
                    Constant c = md.GetConstant(ch);
                    value = ReadConstant(md, c);
                }
                Console.WriteLine("  " + name + " = " + value);
            }
            return 0;
        }

        private static string ReadConstant(MetadataReader md, Constant c)
        {
            BlobReader br = md.GetBlobReader(c.Value);
            switch (c.TypeCode)
            {
                case ConstantTypeCode.SByte: return br.ReadSByte().ToString();
                case ConstantTypeCode.Byte: return br.ReadByte().ToString();
                case ConstantTypeCode.Int16: return br.ReadInt16().ToString();
                case ConstantTypeCode.UInt16: return br.ReadUInt16().ToString();
                case ConstantTypeCode.Int32: return br.ReadInt32().ToString();
                case ConstantTypeCode.UInt32: return br.ReadUInt32().ToString();
                case ConstantTypeCode.Int64: return br.ReadInt64().ToString();
                case ConstantTypeCode.UInt64: return br.ReadUInt64().ToString();
                default: return c.TypeCode.ToString();
            }
        }

        private static string DescribeHandle(MetadataReader md, EntityHandle h)
        {
            switch (h.Kind)
            {
                case HandleKind.TypeReference:
                    TypeReference tr = md.GetTypeReference((TypeReferenceHandle)h);
                    string ns = md.GetString(tr.Namespace);
                    string n = md.GetString(tr.Name);
                    return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
                case HandleKind.TypeDefinition:
                    return FullName(md, md.GetTypeDefinition((TypeDefinitionHandle)h));
                default:
                    return h.Kind.ToString();
            }
        }

        private static string DescribeSignature(MetadataReader md, object sig)
        {
            if (sig is string s)
            {
                return s;
            }
            var list = sig as IEnumerable<string>;
            if (list != null)
            {
                return string.Join(", ", list);
            }
            return sig == null ? "?" : sig.ToString();
        }

        private sealed class SigProvider : ISignatureTypeProvider<string, object>
        {
            public string GetArrayType(string elementType, ArrayShape shape) =>
                elementType + "[]";
            public string GetByReferenceType(string elementType) => "ref " + elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) =>
                "fnptr";
            public string GetGenericInstantiation(string genericType,
                System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
                genericType + "<" + string.Join(",", typeArguments) + ">";
            public string GetGenericMethodParameter(object genericContext, int index) =>
                "!!" + index;
            public string GetGenericTypeParameter(object genericContext, int index) =>
                "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType,
                bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
                typeCode.ToString();
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader,
                TypeDefinitionHandle handle, byte rawTypeKind) =>
                FullName(reader, reader.GetTypeDefinition(handle));
            public string GetTypeFromReference(MetadataReader reader,
                TypeReferenceHandle handle, byte rawTypeKind)
            {
                TypeReference tr = reader.GetTypeReference(handle);
                string ns = reader.GetString(tr.Namespace);
                string n = reader.GetString(tr.Name);
                return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
            }
            public string GetTypeFromSpecification(MetadataReader reader,
                object genericContext, TypeSpecificationHandle handle,
                byte rawTypeKind) => "spec";
        }
    }
}
