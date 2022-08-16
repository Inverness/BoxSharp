using Microsoft.CodeAnalysis;

namespace BoxSharp
{
    internal static class BoxDiagnostics
    {
        internal const string Category = "BoxSharp";

        internal static readonly DiagnosticDescriptor IllegalSymbol =
            new("BOX001",
                "Symbol is not whitelisted",
                "Symbol is not whitelisted: {0}",
                Category,
                DiagnosticSeverity.Error,
                true);

        internal static readonly DiagnosticDescriptor InvalidLoad =
            new("BOX002",
                "Load path not found",
                "Load path not found: {0}",
                Category,
                DiagnosticSeverity.Error,
                true);

        internal static readonly DiagnosticDescriptor DuplicateLoad
            = new("BOX003",
                  "Attempted to load the same script more than once in the same file",
                  "Attempted to load the same script more than once in the same file: {0}",
                  Category,
                  DiagnosticSeverity.Warning,
                  true);

    }
}
