using Microsoft.CodeAnalysis;

namespace BoxSharp
{
    internal static class BoxDiagnostics
    {
        internal static readonly DiagnosticDescriptor IllegalSymbol =
            new("BOX001",
                "Illegal symbol",
                "Symbol is not whitelisted: {0}",
                "BoxSharp",
                DiagnosticSeverity.Error,
                true);

        internal static readonly DiagnosticDescriptor InvalidLoad =
            new("BOX002",
                "Invalid load directive",
                "Invalid load directive: {0}",
                "BoxSharp",
                DiagnosticSeverity.Error,
                true);

    }
}
