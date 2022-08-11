using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BoxSharp
{
    /// <summary>
    /// Provides the results of a script compilation.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ScriptCompileResult<T>
    {
        public ScriptCompileResult(
            CompileStatus result,
            IList<Diagnostic>? diagnostics = null,
            BoxScript<T>? script = null)
        {
            Status = result;
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
            ErrorDiagnostics = diagnostics != null ?
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList() :
                Array.Empty<Diagnostic>();
            Script = script;
        }

        public CompileStatus Status { get; }

        public IList<Diagnostic> Diagnostics { get; }

        public IList<Diagnostic> ErrorDiagnostics { get; }

        public BoxScript<T>? Script { get; }
    }
}
