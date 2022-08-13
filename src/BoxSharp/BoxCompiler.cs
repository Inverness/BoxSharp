using BoxSharp.Runtime.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

#pragma warning disable RS2008

namespace BoxSharp
{
    /// <summary>
    /// Compiles sandboxed scripts.
    /// </summary>
    public class BoxCompiler
    {
        private static readonly DiagnosticDescriptor InvalidLoad =
            new DiagnosticDescriptor("BOX002",
                                     "Invalid load directive",
                                     "Invalid load directive: {0}",
                                     "BoxSharp",
                                     DiagnosticSeverity.Error,
                                     true);

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

        public Task<ScriptCompileResult<T>> Compile<T>(string code, Type? globalsType = null)
        {
            return Compile<T>(SourceText.From(code), globalsType);
        }

        public Task<ScriptCompileResult<T>> Compile<T>(Stream code, Type? globalsType = null)
        {
            return Compile<T>(SourceText.From(code), globalsType);
        }

        public async Task<ScriptCompileResult<T>> Compile<T>(SourceText code, Type? globalsType = null)
        {
            // Using the Roslyn Scripting API is not an option due to the fact that Compilation instances
            // created for scripting have limitations placed on them.
            // The most significant limitation is that syntax trees produced by a #load directive
            // cannot be replaced, which prevents injection of RuntimeGuard calls.

            GidReservation gid = RuntimeGuardInstances.Allocate(_runtimeGuardSettings);

            string assemblyName = "BoxScriptAssembly" + gid.Gid;
            string scriptClassName = "BoxScript" + gid.Gid;

            var diagnostics = new List<Diagnostic>();
            bool hasErrors = false;

            var parseOptions = new CSharpParseOptions(kind: SourceCodeKind.Script);

            // Recursive load the main syntax tree and any files required by a #load directive.
            // The #load directives are stripped out of the syntax trees so that they are not
            // processed by the Compilation instance for reasons described above.

            var readyTrees = new List<SyntaxTree>();
            var pendingTrees = new Stack<SyntaxTree>();
            var pendingLoads = new Stack<LoadDirectiveTriviaSyntax>();

            var mainTree = CSharpSyntaxTree.ParseText(code, parseOptions);

            pendingTrees.Push(mainTree);

            while (pendingTrees.Count > 0)
            {
                SyntaxTree oldTree = pendingTrees.Pop();

                (SyntaxTree newTeee, LoadDirectiveTriviaSyntax[] loadPaths) = await ExtractLoadDirectivesAsync(oldTree);

                foreach (LoadDirectiveTriviaSyntax loadPath in loadPaths)
                {
                    // Meta files are used for code completion when editing scripts, so
                    // they should not be loaded at runtime.
                    if (loadPath.File.ValueText.StartsWith(".meta/"))
                        continue;

                    pendingLoads.Push(loadPath);
                }

                readyTrees.Add(newTeee);

                while (pendingLoads.Count > 0)
                {
                    LoadDirectiveTriviaSyntax loadPath = pendingLoads.Pop();

                    string loadPathText = loadPath.File.ValueText;

                    // TODO: Cache loaded scripts, and also ensure their syntax trees are moved further back in the list
                    string? resolvedLoad = _sourceReferenceResolver.ResolveReference(loadPathText, null);

                    if (resolvedLoad == null)
                    {
                        var diag = Diagnostic.Create(InvalidLoad, loadPath.GetLocation(), loadPathText);

                        diagnostics.Add(diag);

                        hasErrors = true;

                        continue;
                    }

                    using Stream loadedStream = _sourceReferenceResolver.OpenRead(resolvedLoad);

                    SyntaxTree loadedTree = CSharpSyntaxTree.ParseText(SourceText.From(loadedStream), parseOptions);
                        
                    pendingTrees.Push(loadedTree);
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
                                                              metadataReferenceResolver: _metadataReferenceResolver);

            var compilation = CSharpCompilation.Create(assemblyName,
                                                       readyTrees,
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

                    hasErrors = true;
                }
            }

            // If there were no errors so far, rewrite all syntax trees to include calls to RuntimeGuardInterface.

            if (!hasErrors)
            {
                // Generate a class that will hold the static RuntimeGuard instance used in the rest of the code
                // The field will be initialized using the previously allocated GID.
                var scg = new ScriptClassGenerator(gid.Gid, compilation, scriptClassName, globalsType);

                (SyntaxTree genSyntaxTree, string genClassName) = scg.Generate();

                // TODO Consider using OperationWalker instead
                var rewriter = new RuntimeGuardRewriter(gid.Gid, scriptClassName, genClassName, compilation);

                foreach (SyntaxTree oldTree in compilation.SyntaxTrees.ToArray())
                {
                    var oldRoot = (CompilationUnitSyntax) await oldTree.GetRootAsync();

                    var newRoot = (CompilationUnitSyntax) rewriter.Rewrite(oldRoot);

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

            CompileStatus status = hasErrors ? CompileStatus.Failed : CompileStatus.Success;

            if (status == CompileStatus.Failed)
            {
                gid?.Dispose();

                return new ScriptCompileResult<T>(status, diagnostics, null);
            }

            Assembly assembly = Assembly.Load(asmMemoryStream.ToArray(), pdbMemoryStream.ToArray());

            File.WriteAllBytes(@"C:\Projects\dump.dll", asmMemoryStream.ToArray());
            File.WriteAllBytes(@"C:\Projects\dump.pdb", pdbMemoryStream.ToArray());

            Func<Task<object>> runner = GetScriptEntryPoint(assembly, scriptClassName);

            var boxScript = new BoxScript<T>(gid!, runner);

            return new ScriptCompileResult<T>(status, diagnostics, boxScript);
        }

        private static Func<Task<object>> GetScriptEntryPoint(Assembly assembly, string scriptClassName)
        {
            Type scriptClassType = assembly.GetType(scriptClassName, true, false);

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

            SyntaxTriviaList loadTriviaList = firstToken.LeadingTrivia.Where(t => t.IsKind(SyntaxKind.LoadDirectiveTrivia)).ToSyntaxTriviaList();

            SyntaxTriviaList nonLoadTriviaList = firstToken.LeadingTrivia.Where(t => !t.IsKind(SyntaxKind.LoadDirectiveTrivia)).ToSyntaxTriviaList();

            SyntaxToken newFirstToken = firstToken.WithLeadingTrivia(nonLoadTriviaList);

            CompilationUnitSyntax newRoot = root.ReplaceToken(firstToken, newFirstToken);

            SyntaxTree newTree = tree.WithRootAndOptions(newRoot, tree.Options);

            LoadDirectiveTriviaSyntax[] loadFiles = loadTriviaList.Select(l => l.GetStructure())
                                                                  .Cast<LoadDirectiveTriviaSyntax>()
                                                                  .ToArray();

            return (newTree, loadFiles);
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
