using System;
using System.Collections.Generic;

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
            ICollection<string>? errors = null,
            BoxScript<T>? script = null)
        {
            Status = result;
            Errors = errors ?? Array.Empty<string>();
            Script = script;
        }

        public CompileStatus Status { get; }

        public ICollection<string> Errors { get; }

        public BoxScript<T>? Script { get; }
    }
}
