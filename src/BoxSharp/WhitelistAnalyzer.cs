using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace BoxSharp
{
    /// <summary>
    /// Analyzes a syntax tree to determine which symbols meet the whitelist criteria.
    /// </summary>
    internal class WhitelistAnalyzer
    {
        private readonly WhitelistSettings _settings;

        public WhitelistAnalyzer(WhitelistSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Analyzes a syntax tree and returns a list of symbols that are not whitelisted.
        /// </summary>
        /// <param name="compilation">A compilation</param>
        /// <param name="syntaxTree">A syntax tree within the compilation</param>
        /// <returns>A list of symbols that are illegal to use</returns>
        /// <exception cref="InvalidOperationException">No symbols have been whitelisted</exception>
        public async Task<IList<(ISymbol, Diagnostic)>> AnalyzeAsync(
            Compilation compilation,
            SyntaxTree syntaxTree,
            ISet<ISymbol> declaredSymbols)
        {
            var whitelistSymbols = new Dictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            var checkedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (WhitelistSymbol entry in _settings.Symbols)
            {
                ImmutableArray<ISymbol> symbols =
                    DocumentationCommentId.GetSymbolsForDeclarationId(entry.DeclarationId, compilation);

                if (symbols.IsDefaultOrEmpty)
                    continue;

                foreach (ISymbol symbol in symbols)
                {
                    whitelistSymbols[symbol] = entry.IncludeChildren;
                }
            }

            if (whitelistSymbols.Count == 0)
            {
                throw new InvalidOperationException("No whitelisted symbols");
            }

            // TODO Can ignoreAcccessbility be false?
            SemanticModel sem = compilation.GetSemanticModel(syntaxTree, true);

            var results = new List<(ISymbol, Diagnostic)>();

            SyntaxNode root = await syntaxTree.GetRootAsync().ConfigureAwait(false);

            foreach (SyntaxNode node in syntaxTree.GetRoot().DescendantNodesAndSelf())
            {
                SyntaxKind nodeKind = node.Kind();

                if (nodeKind != SyntaxKind.IdentifierName && nodeKind != SyntaxKind.GenericName)
                    continue;

                ISymbol? symbol = sem.GetSymbolInfo(node).Symbol;

                if (symbol == null || !IsRelevantSymbol(symbol.Kind) || !checkedSymbols.Add(symbol))
                    continue;

                foreach (ISymbol s in GetSymbolAndOverridenSymbols(symbol))
                {
                    if (IsWhitelisted(s))
                        continue;

                    var decId = DocumentationCommentId.CreateDeclarationId(s);

                    Diagnostic diag = s.Locations.Length == 1
                        ? Diagnostic.Create(BoxDiagnostics.IllegalSymbol, s.Locations[0], messageArgs: decId)
                        : Diagnostic.Create(BoxDiagnostics.IllegalSymbol, null, s.Locations, messageArgs: decId);

                    results.Add((s, diag));
                }
            }

            return results;

            bool IsWhitelisted(ISymbol symbol)
            {
                if (whitelistSymbols.ContainsKey(symbol) || declaredSymbols.Contains(symbol))
                    return true;

                ISymbol? current = symbol.ContainingSymbol;
                while (current != null)
                {
                    if (whitelistSymbols.TryGetValue(current, out var inherit) && inherit)
                    {
                        whitelistSymbols[symbol] = false;
                        return true;
                    }

                    current = current.ContainingSymbol;
                }

                return false;
            }
        }

        private static bool IsRelevantSymbol(SymbolKind kind)
        {
            return kind switch
            {
                SymbolKind.NamedType or
                SymbolKind.Method or
                SymbolKind.Field or
                SymbolKind.Property or
                SymbolKind.Event => true,
                _ => false,
            };
        }

        private static IEnumerable<ISymbol> GetSymbolAndOverridenSymbols(ISymbol symbol)
        {
            ISymbol? currentSymbol = symbol.OriginalDefinition;

            while (currentSymbol != null)
            {
                yield return currentSymbol;

                // It's possible to have `IsOverride` true and yet have `GetOverriddeMember` returning null when the code is invalid
                // (e.g. base symbol is not marked as `virtual` or `abstract` and current symbol has the `overrides` modifier).
                currentSymbol = currentSymbol.IsOverride
                    ? GetOverriddenMember(currentSymbol)?.OriginalDefinition
                    : null;
            }
        }

        private static ISymbol? GetOverriddenMember(ISymbol symbol)
        {
            Debug.Assert(symbol.IsOverride);

            if (symbol is IMethodSymbol methodSymbol)
                return methodSymbol.OverriddenMethod;

            if (symbol is IPropertySymbol propertySymbol)
                return propertySymbol.OverriddenProperty;

            if (symbol is IEventSymbol eventSymbol)
                return eventSymbol.OverriddenEvent;

            throw new NotImplementedException();
        }
    }
}
