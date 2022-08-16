using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BoxSharp.Runtime.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

#pragma warning disable RS2008

namespace BoxSharp
{
    /// <summary>
    /// Compiles sandboxed scripts.
    /// </summary>
    public class BoxCompiler
    {
        private const string MetaDirName = ".meta";
        private static readonly string MetaDirPart = Path.DirectorySeparatorChar + MetaDirName + Path.DirectorySeparatorChar;

        private static int s_gidCounter;

        private readonly WhitelistSettings _whitelistSettings;

        private readonly RuntimeGuardSettings _runtimeGuardSettings;

        private readonly BoxMetadataReferenceResolver _metadataReferenceResolver = new();

        private readonly WhitelistAnalyzer _analyzer;

        private readonly BoxSourceReferenceResolver _sourceReferenceResolver;

        private readonly bool _isDebug;

        public BoxCompiler(
            WhitelistSettings whitelistSettings,
            RuntimeGuardSettings? runtimeGuardSettings = null,
            IEnumerable<string>? scriptSearchPaths = null,
            string? scriptBaseDirectory = null,
            bool isDebug = false)
        {
            _whitelistSettings = new WhitelistSettings(whitelistSettings);
            _runtimeGuardSettings = new RuntimeGuardSettings(runtimeGuardSettings ?? RuntimeGuardSettings.Default);
            _analyzer = new WhitelistAnalyzer(_whitelistSettings);

            var scriptSearchPathsArray = scriptSearchPaths != null ? scriptSearchPaths.ToImmutableArray() : ImmutableArray<string>.Empty;

            _sourceReferenceResolver = new BoxSourceReferenceResolver(scriptSearchPathsArray, scriptBaseDirectory);

            _isDebug = isDebug;
        }

        public async Task<ScriptCompileResult<T>> CompileFile<T>(string path, Type? globalsType = null)
        {
            using FileStream stream = FileUtilities.OpenAsyncRead(path);

            return await Compile<T>(SourceText.From(stream), globalsType, path);
        }

        public Task<ScriptCompileResult<T>> Compile<T>(string code, Type? globalsType = null, string? path = null)
        {
            return Compile<T>(SourceText.From(code), globalsType, path);
        }

        public async Task<ScriptCompileResult<T>> Compile<T>(SourceText code, Type? globalsType = null, string? path = null)
        {
            // Using the Roslyn Scripting API is not an option due to the fact that Compilation instances
            // created for scripting have limitations placed on them.
            // The most significant limitation is that syntax trees produced by a #load directive
            // cannot be replaced, which prevents injection of RuntimeGuard calls.

            int gid = Interlocked.Increment(ref s_gidCounter);

            string assemblyName = "BoxScriptAssembly" + gid;
            string scriptClassName = "BoxScript" + gid;

            var diagnostics = new List<Diagnostic>();
            bool hasErrors = false;

            (IList<SyntaxTree> loadedTrees, IList<Diagnostic> loadDiagnostics) = await ParseSyntaxTreesAsync(code, path);

            if (loadDiagnostics.Count > 0)
            {
                diagnostics.AddRange(loadDiagnostics);

                hasErrors |= loadDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            }

            OptimizationLevel optLevel = _isDebug ? OptimizationLevel.Debug : OptimizationLevel.Release;

            var compileOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                                              scriptClassName: scriptClassName,
                                                              optimizationLevel: optLevel,
                                                              allowUnsafe: false,
                                                              metadataReferenceResolver: _metadataReferenceResolver,
                                                              nullableContextOptions: NullableContextOptions.Enable);

            // Top level script statements execute in the same order of the syntax tree list.
            // We want scripts loaded last to execute first since other scripts might depend on them, so
            // the loaded trees list is reversed.

            var compilation = CSharpCompilation.Create(assemblyName,
                                                       loadedTrees.Reverse(),
                                                       _whitelistSettings.References,
                                                       compileOptions);

            // Symbols declared in loaded scripts must also be whitelisted, so we'll scan all syntax trees
            // for declarations and build a hash set of symbols.

            var declaredSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                ISet<ISymbol> symbols = await GetDeclaredSymbolsAsync(compilation, tree);

                declaredSymbols.UnionWith(symbols);
            }

            // Analyze all syntax trees with the whitelist analyzer.
            // For each invalid symbol, an error message is added to the errors list.

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                IList<(ISymbol, Diagnostic)> invalidSymbols = await _analyzer.AnalyzeAsync(compilation, tree, declaredSymbols);

                if (invalidSymbols.Count == 0)
                    continue;

                foreach ((ISymbol s, Diagnostic d) in invalidSymbols)
                {
                    // All whitelist analyzer diagnostics are errors
                    Debug.Assert(d.Severity == DiagnosticSeverity.Error);

                    diagnostics.Add(d);
                }

                hasErrors = true;
            }

            // Generate code that will hold the static RuntimeGuard instance used used by the
            // runtime guard rewriter.
            // This will also generate a field to hold the globals type instance, and
            // generate any global members from the globals type instance.
            // We do this even if there are errors because it will cause additional misleading errors otherwise.
            var scg = new ScriptClassGenerator(compilation, scriptClassName, globalsType);

            SyntaxTree genSyntaxTree = scg.Generate();

            // If there were no errors so far, rewrite all syntax trees to include calls to RuntimeGuardInterface.
            if (!hasErrors)
            {
                // TODO Consider using OperationWalker instead
                var rewriter = new RuntimeGuardRewriter(scg.GetRuntimeGuardFieldExpression());

                foreach (SyntaxTree oldTree in compilation.SyntaxTrees.ToArray())
                {
                    var oldRoot = (CompilationUnitSyntax) await oldTree.GetRootAsync();

                    CompilationUnitSyntax newRoot = rewriter.Rewrite(oldRoot);

                    SyntaxTree newTree = oldTree.WithRootAndOptions(newRoot, oldTree.Options);

                    compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);
                }
            }

            compilation = compilation.AddSyntaxTrees(genSyntaxTree);

            // Force the full compilation and check all errors

            foreach (Diagnostic d in compilation.GetDiagnostics())
            {
                diagnostics.Add(d);

                if (d.Severity == DiagnosticSeverity.Error)
                {
                    hasErrors = true;
                }
            }

            var asmMemoryStream = new MemoryStream();
            var pdbMemoryStream = new MemoryStream();

            if (!hasErrors)
            {
                var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);

                EmitResult emitResult = compilation.Emit(asmMemoryStream, pdbMemoryStream, options: emitOptions);

                diagnostics.AddRange(emitResult.Diagnostics);

                hasErrors = !emitResult.Success;
            }

            if (hasErrors)
            {
                return new ScriptCompileResult<T>(CompileStatus.Failed, diagnostics, null);
            }

            Assembly assembly = Assembly.Load(asmMemoryStream.ToArray(), pdbMemoryStream.ToArray());

            Type scriptClassType = assembly.GetType(scriptClassName, true, false);

            Func<Task<object>> runner = GetScriptEntryPoint(scriptClassType);

            Action<RuntimeGuard> runtimeGuardSetter = ReflectionUtilities.MakeStaticFieldSetter<RuntimeGuard>(scriptClassType, ScriptClassGenerator.RuntimeGuardFieldName);
            Action<object?>? globalsSetter = null;

            if (globalsType != null)
            {
                globalsSetter = ReflectionUtilities.MakeStaticFieldSetter(scriptClassType, ScriptClassGenerator.GlobalsFieldName);
            }

            var rg = new RuntimeGuard();
            rg.Initialize(_runtimeGuardSettings);

            var boxScript = new BoxScript<T>(rg, runner, runtimeGuardSetter, globalsSetter);

            return new ScriptCompileResult<T>(CompileStatus.Success, diagnostics, boxScript);
        }

        /// <summary>
        /// Recursively parse the main script syntax tree and any other script syntax trees specified
        /// by load directives. Any load directives found will be stripped from the syntax trees
        /// after being processed.
        /// </summary>
        /// <param name="mainCode">The main code</param>
        /// <param name="mainPath">The path to the main code</param>
        /// <returns>A list of syntax trees in order of load</returns>
        private async Task<(IList<SyntaxTree>, IList<Diagnostic>)> ParseSyntaxTreesAsync(SourceText mainCode, string? mainPath)
        {
            var parseOptions = new CSharpParseOptions(kind: SourceCodeKind.Script);

            var diagnostics = new List<Diagnostic>();
            var readyTrees = new List<(SyntaxTree tree, string? path)>();
            var pendingTrees = new Stack<(SyntaxTree tree, string? path)>();

            if (!string.IsNullOrEmpty(mainPath))
                mainPath = Path.GetFullPath(mainPath);

            SyntaxTree mainTree = CSharpSyntaxTree.ParseText(mainCode, parseOptions, mainPath ?? "");

            pendingTrees.Push((mainTree, mainPath));

            while (pendingTrees.Count > 0)
            {
                (SyntaxTree oldTree, string? sourcePath) = pendingTrees.Pop();

                (SyntaxTree newTree, IList<LoadDirectiveTriviaSyntax> loadPaths) = await ExtractLoadDirectivesAsync(oldTree);

                readyTrees.Add((newTree, sourcePath));

                foreach (LoadDirectiveTriviaSyntax load in loadPaths)
                {
                    string loadPath = load.File.ValueText;

                    string? resolvedLoadPath = _sourceReferenceResolver.ResolveReference(loadPath, sourcePath);

                    if (resolvedLoadPath == null)
                    {
                        var diag = Diagnostic.Create(BoxDiagnostics.InvalidLoad, load.GetLocation(), loadPath);

                        diagnostics.Add(diag);

                        continue;
                    }

                    // Meta files are used for code completion when editing scripts, so
                    // they should not be loaded at runtime.
                    if (resolvedLoadPath.IndexOf(MetaDirPart, StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    // Check if the script was already loaded
                    // TODO May need more complex dependency graph resolution for top level statement ordering
                    if (readyTrees.Any(i => i.path == resolvedLoadPath))
                        continue;

                    // Loading the same script twice in the same file isn't allowed
                    if (pendingTrees.Any(i => i.path == resolvedLoadPath))
                    {
                        var diag = Diagnostic.Create(BoxDiagnostics.DuplicateLoad, load.GetLocation(), loadPath);

                        diagnostics.Add(diag);

                        continue;
                    }

                    using Stream loadedStream = _sourceReferenceResolver.OpenRead(resolvedLoadPath);

                    SyntaxTree loadedTree = CSharpSyntaxTree.ParseText(SourceText.From(loadedStream),
                                                                       parseOptions,
                                                                       resolvedLoadPath);

                    pendingTrees.Push((loadedTree, resolvedLoadPath));
                }
            }

            return (readyTrees.Select(i => i.tree).ToList(), diagnostics);
        }

        /// <summary>
        /// Gets a set of all symbols declared in the syntax tree.
        /// </summary>
        /// <param name="compilation">The tree's compilation unit</param>
        /// <param name="tree">A syntax tree</param>
        /// <returns>A set of all declared symbols</returns>
        private static async Task<ISet<ISymbol>> GetDeclaredSymbolsAsync(Compilation compilation, SyntaxTree tree)
        {
            var declaredSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            SyntaxNode root = await tree.GetRootAsync();

            SemanticModel? semanticModel = null;

            foreach (SyntaxNode node in root.DescendantNodesAndSelf())
            {
                if (node is not MemberDeclarationSyntax && node is not VariableDeclaratorSyntax)
                    continue;

                semanticModel ??= compilation.GetSemanticModel(tree);

                ISymbol? symbol = semanticModel.GetDeclaredSymbol(node);

                if (symbol != null)
                {
                    declaredSymbols.Add(symbol);
                }
            }

            return declaredSymbols;
        }

        private static Func<Task<object>> GetScriptEntryPoint(Type scriptClassType)
        {
            MethodInfo? entryPointMethod = scriptClassType.GetMethod("\u003CInitialize\u003E", BindingFlags.Instance | BindingFlags.NonPublic);

            if (entryPointMethod == null)
                throw new EntryPointNotFoundException("Script entry point not found");

            object scriptClassInst = Activator.CreateInstance(scriptClassType, true);

            var entryPointDelegate = (Func<Task<object>>) entryPointMethod.CreateDelegate(typeof(Func<Task<object>>), scriptClassInst);

            return entryPointDelegate;
        }

        private static async Task<(SyntaxTree, IList<LoadDirectiveTriviaSyntax>)> ExtractLoadDirectivesAsync(SyntaxTree tree)
        {
            var root = (CompilationUnitSyntax) await tree.GetRootAsync();

            // Load directives are always part of the first token
            SyntaxToken firstToken = root.GetFirstToken(includeZeroWidth: true);

            if (!firstToken.LeadingTrivia.Any(t => t.IsKind(SyntaxKind.LoadDirectiveTrivia)))
                return (tree, Array.Empty<LoadDirectiveTriviaSyntax>());

            LoadDirectiveTriviaSyntax[] loadDirectives =
                firstToken.LeadingTrivia.Where(t => t.IsKind(SyntaxKind.LoadDirectiveTrivia))
                                        .Select(l => l.GetStructure())
                                        .Cast<LoadDirectiveTriviaSyntax>()
                                        .ToArray();

            // Rebuild the leading trivia without the load directives. Must keep the same number of lines in the file
            // to ensure that debugging works correctly.
            SyntaxTriviaList newLeadingTrivia = SyntaxTriviaList.Empty;

            foreach (SyntaxTrivia trivia in firstToken.LeadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.LoadDirectiveTrivia))
                {
                    // TODO match the existing type of line endings
                    newLeadingTrivia = newLeadingTrivia.Add(SyntaxFactory.CarriageReturnLineFeed);
                }
                else
                {
                    newLeadingTrivia = newLeadingTrivia.Add(trivia);
                }
            }

            SyntaxToken newFirstToken = firstToken.WithLeadingTrivia(newLeadingTrivia);

            CompilationUnitSyntax newRoot = root.ReplaceToken(firstToken, newFirstToken);

            SyntaxTree newTree = tree.WithRootAndOptions(newRoot, tree.Options);

            return (newTree, loadDirectives);
        }

        private class BoxMetadataReferenceResolver : MetadataReferenceResolver
        {
            public override bool ResolveMissingAssemblies => true;

            public override bool Equals(object? other)
            {
                return other is BoxMetadataReferenceResolver;
            }

            public override int GetHashCode()
            {
                // All instances are equal for now
                return typeof(BoxMetadataReferenceResolver).GetHashCode();
            }

            public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties)
            {
                return ImmutableArray.Create<PortableExecutableReference>();
            }

            public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity)
            {
                if (definition is PortableExecutableReference pe)
                    return pe;
                return null;
            }
        }

        private class BoxSourceReferenceResolver : SourceFileResolver
        {
            public BoxSourceReferenceResolver(IEnumerable<string>? searchPaths, string? baseDirectory) : base(searchPaths!, baseDirectory)
            {
            }

            public BoxSourceReferenceResolver(ImmutableArray<string> searchPaths, string? baseDirectory) : base(searchPaths, baseDirectory)
            {
            }

            public BoxSourceReferenceResolver(ImmutableArray<string> searchPaths, string? baseDirectory, ImmutableArray<KeyValuePair<string, string>> pathMap) : base(searchPaths, baseDirectory, pathMap)
            {
            }

            //public override string? NormalizePath(string path, string? baseFilePath)
            //{
            //    return base.NormalizePath(path, baseFilePath);
            //}

            public override Stream OpenRead(string resolvedPath)
            {
                if (resolvedPath.IndexOf(MetaDirPart, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new ArgumentException("Attempted to open meta script: " + resolvedPath);

                return base.OpenRead(resolvedPath);
            }

            public override string? ResolveReference(string path, string? baseFilePath)
            {
                //if (path.StartsWith(".meta/"))
                //    throw new ArgumentException("Attempted to resolve meta script: " + path);

                return base.ResolveReference(path, baseFilePath);
            }
        }
    }
}
