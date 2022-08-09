using BoxSharp.Runtime.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BoxSharp
{
    /// <summary>
    /// Compiles sandboxed scripts.
    /// </summary>
    public class BoxCompiler
    {
        public const string ScriptClassName = "BoxScript.MainClass";

        private static readonly Action<Script, Compilation> s_setScriptCompilation =
            ReflectionUtilities.MakeFieldSetter<Script, Compilation>("_lazyCompilation");

        private readonly WhitelistSettings _whitelistSettings;

        private readonly RuntimeGuardSettings _runtimeGuardSettings;

        private readonly BoxMetadataReferenceResolver _metadataReferenceResolver = new();

        private readonly WhitelistAnalyzer _analyzer;

        private readonly ScriptOptions _scriptOptions;

        public BoxCompiler(WhitelistSettings whitelistSettings, RuntimeGuardSettings? runtimeGuardSettings = null)
        {
            _whitelistSettings = new WhitelistSettings(whitelistSettings);
            _runtimeGuardSettings = new RuntimeGuardSettings(runtimeGuardSettings ?? RuntimeGuardSettings.Default);
            _analyzer = new WhitelistAnalyzer(_whitelistSettings);

            // ScriptOptions comes with several default references that we're replacing by using WithReferences()
            // instead of AddReferences()
            ScriptOptions options = ScriptOptions.Default.WithReferences(_whitelistSettings.References)
                                                         .WithMetadataResolver(_metadataReferenceResolver);

#if DEBUG
            options = options.WithOptimizationLevel(OptimizationLevel.Debug);
#else
            options = options.WithOptimizationLevel(OptimizationLevel.Release);
#endif

            _scriptOptions = options;
        }

        public async Task<ScriptCompileResult<T>> Compile<T>(string code, Type? globalsType = null)
        {
            Script<T> script = CSharpScript.Create<T>(code, _scriptOptions, globalsType);

            Compilation compilation = script.GetCompilation();

            var errors = new List<string>();

            // Analyze all syntax trees with the whitelist analyzer.
            // For each invalid symbol, an error message is added to the errors list.

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                IList<ISymbol> invalidSymbols = await _analyzer.AnalyzeAsync(compilation, tree);

                if (invalidSymbols.Count == 0)
                    continue;

                foreach (ISymbol s in invalidSymbols)
                {
                    var err = $"Symbol not allowed: {s.ToDisplayString()} ({DocumentationCommentId.CreateDeclarationId(s)})";

                    errors.Add(err);
                }
            }

            // If there were no invalid symbols, rewrite all syntax trees to include
            // calls to RuntimeGuardInterface.
            GidReservation? gid = null;

            if (errors.Count == 0)
            {
                // Create a RuntimeGuard instance and return a GID (Guard ID) that can be used to retrieve it in generated code

                gid = RuntimeGuardInstances.Allocate(_runtimeGuardSettings);

                // Generate a class that will hold the static RuntimeGuard instance used in the rest of the code
                // The field will be initialized using the previously allocated GID.

                (SyntaxTree genSyntaxTree, string genClassName) = ScriptClassGenerator.Generate(gid.Gid);

                var genRoot = (CompilationUnitSyntax) genSyntaxTree.GetRoot();

                // TODO Consider using OperationWalker instead
                var rewriter = new RuntimeGuardRewriter(gid.Gid, compilation.ScriptClass?.Name, genClassName, compilation);

                // Scripts only have one syntax tree
                SyntaxTree oldTree = compilation.SyntaxTrees.Single();

                var oldRoot = (CompilationUnitSyntax) await compilation.SyntaxTrees.First().GetRootAsync();

                var newRoot = (CompilationUnitSyntax) rewriter.Rewrite(oldRoot);

                // Scripts cannot have additional syntax trees added to the compilation, so we'll need to insert
                // generated code into the existing syntax tree.
                CompilationUnitSyntax newRootWithGen = newRoot.WithMembers(newRoot.Members.InsertRange(0, genRoot.Members));

                SyntaxTree newTree = oldTree.WithRootAndOptions(newRootWithGen, oldTree.Options);

                compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

                // No normal way to recreate a script with a new compilation, so will need to
                // set the private field directly
                s_setScriptCompilation(script, compilation);
            }

            // Force the full compilation and check all errors

            foreach (Diagnostic d in script.Compile())
            {
                if (d.Severity == DiagnosticSeverity.Error)
                {
                    errors.Add(d.ToString());
                }
            }

            CompileStatus status = errors.Count > 0 ? CompileStatus.Failed : CompileStatus.Success;

            if (status == CompileStatus.Failed)
            {
                gid?.Dispose();

                return new ScriptCompileResult<T>(status, errors, null);
            }

            Debug.Assert(gid != null);

            var boxScript = new BoxScript<T>(gid!, script);

            return new ScriptCompileResult<T>(status, null, boxScript);
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
    }
}
