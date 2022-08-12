using BoxSharp.Runtime.Internal;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BoxSharp
{
    /// <summary>
    /// Contains settings for the compiler's whitelisting functionality.
    /// </summary>
    public sealed class WhitelistSettings
    {
        private static readonly IComparer<MetadataReference> s_referenceComparer =
            Comparer<MetadataReference>.Create((a, b) => string.Compare(a.Display, b.Display));

        private readonly SortedSet<WhitelistSymbol> _symbols;
        private readonly SortedSet<MetadataReference> _references;

        public WhitelistSettings()
        {
            _symbols = new();
            _references = new(s_referenceComparer);

            AddReferenceByType(typeof(object));
            AddReferenceByType(typeof(RuntimeGuardInterface));
        }

        public WhitelistSettings(WhitelistSettings other)
        {
            _symbols = new(other._symbols);
            _references = new(other._references, s_referenceComparer);
        }

        public IReadOnlyCollection<WhitelistSymbol> Symbols => _symbols;

        public IReadOnlyCollection<MetadataReference> References => _references;

        public void AddReference(MetadataReference reference)
        {
            _references.Add(reference);
        }

        public void AddSdkReference(string name)
        {
            string sdkDir = Path.GetDirectoryName(typeof(object).Assembly.Location);

            string asmFileName = name + ".dll";

            string asmPath = Path.Combine(sdkDir, asmFileName);

            PortableExecutableReference asmRef = MetadataReference.CreateFromFile(asmPath);

            AddReference(asmRef);
        }

        public void AddReferenceByType(Type type)
        {
            var location = type.Assembly.Location;

            PortableExecutableReference mr = MetadataReference.CreateFromFile(location);

            _references.Add(mr);
        }

        public void AddSymbol(WhitelistSymbol entry)
        {
            _symbols.Add(entry);
        }

        public void AddSymbol(Type type, bool includeChildren = false)
        {
            string decId = GetDeclarationId(type);

            var entry = new WhitelistSymbol(decId, includeChildren);

            if (!_symbols.Add(entry))
                return;

            if (type.BaseType != null)
            {
                AddSymbol(type.BaseType, false);
            }

            if (type.DeclaringType != null)
            {
                AddSymbol(type.DeclaringType, false);
            }

            if (includeChildren)
            {
                foreach (Type n in type.GetNestedTypes())
                {
                    AddSymbol(n, true);
                }
            }
        }

        public async Task LoadSymbolFileAsync(string path)
        {
            using FileStream stream = FileUtilities.OpenAsyncRead(path);
            await LoadSymbolFileAsync(stream);
        }

        public async Task LoadSymbolFileAsync(Stream stream)
        {
            IList<WhitelistSymbol> entries = await WhilelistFileReader.LoadAsync(stream);

            foreach (WhitelistSymbol e in entries)
            {
                _symbols.Add(e);
            }
        }

        private static string GetDeclarationId(Type type)
        {
            return "T:" + type.FullName.Replace("+", ".");
        }
    }
}
