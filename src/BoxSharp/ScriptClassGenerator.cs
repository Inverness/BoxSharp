using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BoxSharp.Runtime.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
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
        internal static readonly string RuntimeGuardFieldName = BoxSyntaxFactory.MakeSpecialName("RuntimeGuard");
        internal static readonly string GlobalsFieldName = BoxSyntaxFactory.MakeSpecialName("Globals");

        private readonly int _gid;
        private readonly Compilation _compilation;
        private readonly string _scriptClassName;
        private readonly Type? _globalsType;

        internal ScriptClassGenerator(int gid, Compilation compilation, string scriptClassName, Type? globalsType)
        {
            Debug.Assert(globalsType == null || globalsType.IsClass || globalsType.IsInterface);

            _gid = gid;
            _compilation = compilation;
            _scriptClassName = scriptClassName;
            _globalsType = globalsType;
        }

        internal (SyntaxTree syntaxTree, string genClassName) Generate()
        {
            string className = MakeGeneratedClassName(_gid);

            CompilationUnitSyntax cu = MakeGeneratedCompilationUnit(_gid, className);

            if (_globalsType != null)
            {
                FieldDeclarationSyntax globalsField = GenerateGlobalsField(className);

                cu = cu.AddMembers(globalsField);

                MemberDeclarationSyntax[] globalMembers = GenerateGlobalMembers();

                if (globalMembers.Length > 0)
                {
                    cu = cu.AddMembers(globalMembers);
                }
            }

            cu = cu.NormalizeWhitespace();

            return (CSharpSyntaxTree.Create(cu, new CSharpParseOptions(kind: SourceCodeKind.Script)), className);
        }

        private FieldDeclarationSyntax GenerateGlobalsField(string genClassName)
        {
            Debug.Assert(_globalsType != null);

            INamedTypeSymbol? globalsTypeSymbol = _compilation.GetTypeByMetadataName(_globalsType!.FullName);

            if (globalsTypeSymbol == null)
                throw new InvalidOperationException("Could not get globals type symbol");

            SyntaxGenerator sg = BoxSyntaxFactory.SyntaxGenerator;

            SyntaxNode globalsTypeSyntax = sg.TypeExpression(globalsTypeSymbol);

            MemberAccessExpressionSyntax rgAccess = BoxSyntaxFactory.MakeRgAccess(_scriptClassName, genClassName);

            SyntaxNode getRgGlobalsProp = sg.MemberAccessExpression(rgAccess, nameof(RuntimeGuard.Globals));

            return (FieldDeclarationSyntax) sg.FieldDeclaration(GlobalsFieldName,
                                                                globalsTypeSyntax,
                                                                Accessibility.Public,
                                                                DeclarationModifiers.Static,
                                                                sg.CastExpression(globalsTypeSyntax, getRgGlobalsProp));
        }

        private MemberDeclarationSyntax[] GenerateGlobalMembers()
        {
            Debug.Assert(_globalsType != null);

            INamedTypeSymbol globalsTypeSymbol = _compilation.GetTypeByMetadataName(_globalsType!.FullName)!;

            SyntaxGenerator sg = BoxSyntaxFactory.SyntaxGenerator;

            var globalsAccess = (ExpressionSyntax) sg.IdentifierName(GlobalsFieldName);

            var newMembers = new List<MemberDeclarationSyntax>();

            foreach (ISymbol member in globalsTypeSymbol.GetMembers())
            {
                if (member is IMethodSymbol method)
                {
                    if (method.IsStatic || method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    List<SyntaxNode> args = method.Parameters.Select(p => sg.Argument(p.RefKind, sg.IdentifierName(p.Name))).ToList();

                    SyntaxNode methodAccess = sg.MemberAccessExpression(globalsAccess, method.Name);

                    SyntaxNode invokeMethod = sg.InvocationExpression(methodAccess, args);

                    SyntaxNode stmt = method.ReturnsVoid ? sg.ExpressionStatement(invokeMethod) : sg.ReturnStatement(invokeMethod);

                    var newMethod = (MemberDeclarationSyntax) sg.MethodDeclaration(method, new[] { stmt });

                    newMethod = newMethod.AddModifiers(Token(SyntaxKind.StaticKeyword));

                    newMembers.Add(newMethod);
                }
                else if (member is IPropertySymbol prop)
                {
                    if (prop.IsStatic || prop.IsIndexer || prop.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    SyntaxNode? getStmt = null;
                    SyntaxNode? setStmt = null;

                    if (prop.GetMethod != null && IsPublicOrDefault(prop.GetMethod.DeclaredAccessibility))
                    {
                        getStmt = sg.ReturnStatement(sg.MemberAccessExpression(globalsAccess, prop.Name));
                    }

                    if (prop.SetMethod != null && IsPublicOrDefault(prop.SetMethod.DeclaredAccessibility))
                    {
                        setStmt = sg.AssignmentStatement(sg.MemberAccessExpression(globalsAccess, prop.Name),
                                                         sg.IdentifierName("value"));
                    }

                    var newProp = (MemberDeclarationSyntax) sg.PropertyDeclaration(prop,
                                                                                   getStmt != null ? new[] { getStmt } : null,
                                                                                   setStmt != null ? new[] { setStmt } : null);

                    newProp = newProp.AddModifiers(Token(SyntaxKind.StaticKeyword));

                    newMembers.Add(newProp);
                }
            }

            return newMembers.ToArray();

            bool IsPublicOrDefault(Accessibility accessibility)
            {
                return accessibility == Accessibility.NotApplicable || accessibility == Accessibility.Public;
            }
        }

        private static bool IsAllowedGlobalSymbol(SymbolKind kind)
        {
            switch (kind)
            {
                case SymbolKind.Event:
                case SymbolKind.Field:
                case SymbolKind.Method:
                case SymbolKind.NamedType:
                case SymbolKind.Property:
                    return true;
                default:
                    return false;
            }
        }

        private static string MakeGeneratedClassName(int gid)
        {
            return "_BoxScriptGenerated" + gid;
        }

        private static CompilationUnitSyntax MakeGeneratedCompilationUnit(int gid, string className)
        {
            FieldDeclarationSyntax rgField =
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
                            Token(SyntaxKind.StaticKeyword)}));

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
                                rgField))));
        }
    }
}
