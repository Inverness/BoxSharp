using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BoxSharp
{
    /// <summary>
    /// Generates the "_BoxScriptGenerated#" class for each script.
    /// 
    /// Currently this contains the static RuntimeGuard instance field that is initialized and used by RuntimeGuardInterface.
    /// </summary>
    internal class ScriptClassGenerator
    {
        internal const string RuntimeGuardFieldName = "RuntimeGuard";

        internal static (SyntaxTree syntaxTree, string genClassName) Generate(int gid)
        {
            string className = MakeGeneratedClassName(gid);

            CompilationUnitSyntax cu = MakeGeneratedCompilationUnit(gid, className).NormalizeWhitespace();

            return (CSharpSyntaxTree.Create(cu, new CSharpParseOptions(kind: SourceCodeKind.Script)), className);
        }

        private static string MakeGeneratedClassName(int gid)
        {
            return "_BoxScriptGenerated" + gid;
        }

        private static CompilationUnitSyntax MakeGeneratedCompilationUnit(int gid, string className)
        {
            return CompilationUnit()
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        ClassDeclaration(className)
                        .WithModifiers(
                            TokenList(
                                new[]{
                                    Token(SyntaxKind.PublicKeyword),
                                    Token(SyntaxKind.StaticKeyword)}))
                        .WithMembers(
                            SingletonList<MemberDeclarationSyntax>(
                                FieldDeclaration(
                                    VariableDeclaration(
                                        QualifiedName(
                                            QualifiedName(
                                                QualifiedName(
                                                    AliasQualifiedName(
                                                        IdentifierName(
                                                            Token(SyntaxKind.GlobalKeyword)),
                                                        IdentifierName("BoxSharp")),
                                                    IdentifierName("Runtime")),
                                                IdentifierName("Internal")),
                                            IdentifierName("RuntimeGuard")))
                                    .WithVariables(
                                        SingletonSeparatedList<VariableDeclaratorSyntax>(
                                            VariableDeclarator(
                                                Identifier(RuntimeGuardFieldName))
                                            .WithInitializer(
                                                EqualsValueClause(
                                                    InvocationExpression(
                                                        MemberAccessExpression(
                                                            SyntaxKind.SimpleMemberAccessExpression,
                                                            MemberAccessExpression(
                                                                SyntaxKind.SimpleMemberAccessExpression,
                                                                MemberAccessExpression(
                                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                                    MemberAccessExpression(
                                                                        SyntaxKind.SimpleMemberAccessExpression,
                                                                        AliasQualifiedName(
                                                                            IdentifierName(
                                                                                Token(SyntaxKind.GlobalKeyword)),
                                                                            IdentifierName("BoxSharp")),
                                                                        IdentifierName("Runtime")),
                                                                    IdentifierName("Internal")),
                                                                IdentifierName("RuntimeGuardInterface")),
                                                            IdentifierName("InitializeStaticField")))
                                                    .WithArgumentList(
                                                        ArgumentList(
                                                            SingletonSeparatedList<ArgumentSyntax>(
                                                                Argument(
                                                                    LiteralExpression(
                                                                        SyntaxKind.NumericLiteralExpression,
                                                                        Literal(gid)))))))))))
                                .WithModifiers(
                                    TokenList(
                                        new[]{
                                            Token(SyntaxKind.PublicKeyword),
                                            Token(SyntaxKind.StaticKeyword)}))))));
        }
    }
}
