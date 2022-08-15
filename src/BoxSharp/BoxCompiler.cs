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

            var parseOptions = new CSharpParseOptions(kind: SourceCodeKind.Script);

            // Recursive load the main syntax tree and any files required by a #load directive.
            // The #load directives are stripped out of the syntax trees so that they are not
            // processed by the Compilation instance for reasons described above.

            var readyTrees = new List<(SyntaxTree tree, string? path)>();
            var pendingTrees = new Stack<(SyntaxTree tree, string? path)>();
            var pendingLoads = new Stack<LoadDirectiveTriviaSyntax>();

            if (!string.IsNullOrEmpty(path))
                path = Path.GetFullPath(path);

            SyntaxTree mainTree = CSharpSyntaxTree.ParseText(code, parseOptions, path ?? "");

            pendingTrees.Push((mainTree, path));

            while (pendingTrees.Count > 0)
            {
                (SyntaxTree oldTree, string? sourceLoad) = pendingTrees.Pop();

                (SyntaxTree newTree, LoadDirectiveTriviaSyntax[] loadPaths) = await ExtractLoadDirectivesAsync(oldTree);

                foreach (LoadDirectiveTriviaSyntax loadPath in loadPaths)
                {
                    // Meta files are used for code completion when editing scripts, so
                    // they should not be loaded at runtime.
                    if (loadPath.File.ValueText.StartsWith(".meta/"))
                        continue;

                    pendingLoads.Push(loadPath);
                }

                readyTrees.Add((newTree, sourceLoad));

                while (pendingLoads.Count > 0)
                {
                    LoadDirectiveTriviaSyntax load = pendingLoads.Pop();

                    string loadPath = load.File.ValueText;

                    // TODO: Cache loaded scripts, and also ensure their syntax trees are moved further back in the list
                    string? resolvedLoadPath = _sourceReferenceResolver.ResolveReference(loadPath, sourceLoad);

                    if (resolvedLoadPath == null)
                    {
                        var diag = Diagnostic.Create(BoxDiagnostics.InvalidLoad, load.GetLocation(), loadPath);

                        diagnostics.Add(diag);

                        hasErrors = true;

                        continue;
                    }

                    // Check if the script was already loaded
                    // TODO May need more complex dependency graph resolution for top level statement ordering
                    if (readyTrees.Any(i => i.path == resolvedLoadPath))
                        continue;

                    // Loading the same script twice in the same file isn't allowed
                    // TODO diagnostic error for load specified twice
                    if (pendingTrees.Any(i => i.path == resolvedLoadPath))
                        continue;

                    using Stream loadedStream = _sourceReferenceResolver.OpenRead(resolvedLoadPath);

                    SyntaxTree loadedTree = CSharpSyntaxTree.ParseText(SourceText.From(loadedStream),
                                                                       parseOptions,
                                                                       resolvedLoadPath);
                        
                    pendingTrees.Push((loadedTree, resolvedLoadPath));
                }
            }

            // The order of syntax trees affects the order that top level script statements are executed.
            // We want scripts loaded last to execute first since other scripts might depend on them.
            readyTrees.Reverse();

            OptimizationLevel optLevel = _isDebug ? OptimizationLevel.Debug : OptimizationLevel.Release;

            var compileOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                                              scriptClassName: scriptClassName,
                                                              optimizationLevel: optLevel,
                                                              allowUnsafe: false,
                                                              metadataReferenceResolver: _metadataReferenceResolver,
                                                              nullableContextOptions: NullableContextOptions.Enable);

            var compilation = CSharpCompilation.Create(assemblyName,
                                                       readyTrees.Select(i => i.tree),
                                                       _whitelistSettings.References,
                                                       compileOptions);

            // Symbols declared in loaded scripts must also be whitelisted, so we'll scan all syntax trees
            // for declarations and build a hash set of symbols.

            var declaredSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SyntaxNode root = await tree.GetRootAsync();

                foreach (SyntaxNode node in root.DescendantNodesAndSelf())
                {
                    SemanticModel? semanticModel = null;

                    if (node is not MemberDeclarationSyntax && node is not VariableDeclaratorSyntax)
                        continue;

                    semanticModel ??= compilation.GetSemanticModel(tree);

                    ISymbol? symbol = semanticModel.GetDeclaredSymbol(node);

                    if (symbol != null)
                    {
                        declaredSymbols.Add(symbol);
                    }
                }
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

            // If there were no errors so far, rewrite all syntax trees to include calls to RuntimeGuardInterface.

            if (!hasErrors)
            {
                // Generate a class that will hold the static RuntimeGuard instance used in the rest of the code
                // The field will be initialized using the previously allocated GID.
                var scg = new ScriptClassGenerator(compilation, scriptClassName, globalsType);

                SyntaxTree genSyntaxTree = scg.Generate();

                // TODO Consider using OperationWalker instead
                var rewriter = new RuntimeGuardRewriter(scg.GetRuntimeGuardFieldExpression());

                foreach (SyntaxTree oldTree in compilation.SyntaxTrees.ToArray())
                {
                    var oldRoot = (CompilationUnitSyntax) await oldTree.GetRootAsync();

                    CompilationUnitSyntax newRoot = rewriter.Rewrite(oldRoot);

                    SyntaxTree newTree = oldTree.WithRootAndOptions(newRoot, oldTree.Options);

                    compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);
                }

                compilation = compilation.AddSyntaxTrees(genSyntaxTree);
            }

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

        private static Func<Task<object>> GetScriptEntryPoint(Type scriptClassType)
        {
            MethodInfo? entryPointMethod = scriptClassType.GetMethod("\u003CInitialize\u003E", BindingFlags.Instance | BindingFlags.NonPublic);

            if (entryPointMethod == null)
                throw new EntryPointNotFoundException("Script entry point not found");

            object scriptClassInst = Activator.CreateInstance(scriptClassType, true);

            var entryPointDelegate = (Func<Task<object>>) entryPointMethod.CreateDelegate(typeof(Func<Task<object>>), scriptClassInst);

            return entryPointDelegate;
        }

        private static async Task<(SyntaxTree, LoadDirectiveTriviaSyntax[])> ExtractLoadDirectivesAsync(SyntaxTree tree)
        {
            var root = (CompilationUnitSyntax) await tree.GetRootAsync();

            // Load directives are always part of the first token
            SyntaxToken firstToken = root.GetFirstToken(includeZeroWidth: true);

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
                if (resolvedPath.StartsWith(".meta/"))
                    throw new ArgumentException("Attempted to open meta script: " + resolvedPath);

                return base.OpenRead(resolvedPath);
            }

            public override string? ResolveReference(string path, string? baseFilePath)
            {
                if (path.StartsWith(".meta/"))
                    throw new ArgumentException("Attempted to resolve meta script: " + path);

                return base.ResolveReference(path, baseFilePath);
            }
        }
    }
}
