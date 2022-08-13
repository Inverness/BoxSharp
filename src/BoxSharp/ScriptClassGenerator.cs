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

        internal SyntaxTree Generate()
        {
            var members = new List<MemberDeclarationSyntax>();

            FieldDeclarationSyntax runtimeGuardField = GenerateRuntimeGuardField();

            members.Add(runtimeGuardField);

            if (_globalsType != null)
            {
                FieldDeclarationSyntax globalsField = GenerateGlobalsField();

                members.Add(globalsField);

                MemberDeclarationSyntax[] globalMembers = GenerateGlobalMembers();

                members.AddRange(globalMembers);
            }

            CompilationUnitSyntax cu = CompilationUnit().AddMembers(members.ToArray()).NormalizeWhitespace();

            return CSharpSyntaxTree.Create(cu, new CSharpParseOptions(kind: SourceCodeKind.Script));
        }

        internal ExpressionSyntax GetRuntimeGuardFieldExpression()
        {
            return BoxSyntaxFactory.GlobalMemberAccess(_scriptClassName, RuntimeGuardFieldName);
        }

        private FieldDeclarationSyntax GenerateRuntimeGuardField()
        {
            SyntaxGenerator sg = BoxSyntaxFactory.SyntaxGenerator;

            INamedTypeSymbol? runtimeGuardType = _compilation.GetTypeByMetadataName(typeof(RuntimeGuard).FullName);

            if (runtimeGuardType == null)
                throw new InvalidOperationException("Could not get RuntimeGuard symbol");

            SyntaxNode runtimeGuardTypeSyntax = sg.TypeExpression(runtimeGuardType);

            SyntaxNode field = sg.FieldDeclaration(RuntimeGuardFieldName,
                                                   runtimeGuardTypeSyntax,
                                                   Accessibility.Public,
                                                   DeclarationModifiers.Static);

            return (FieldDeclarationSyntax) field;
        }

        private FieldDeclarationSyntax GenerateGlobalsField()
        {
            Debug.Assert(_globalsType != null);

            INamedTypeSymbol? globalsTypeSymbol = _compilation.GetTypeByMetadataName(_globalsType!.FullName);

            if (globalsTypeSymbol == null)
                throw new InvalidOperationException("Could not get globals type symbol");

            SyntaxGenerator sg = BoxSyntaxFactory.SyntaxGenerator;

            SyntaxNode globalsTypeSyntax = sg.TypeExpression(globalsTypeSymbol);

            return (FieldDeclarationSyntax) sg.FieldDeclaration(GlobalsFieldName,
                                                                globalsTypeSyntax,
                                                                Accessibility.Public,
                                                                DeclarationModifiers.Static);
        }

        /// <summary>
        /// Examines the globals type to find members that can have declarations added as a global statement.
        /// 
        /// A
        /// </summary>
        /// <returns></returns>
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

                    var newMethod = (MethodDeclarationSyntax) sg.MethodDeclaration(method, new[] { stmt });

                    newMethod = newMethod.AddModifiers(Token(SyntaxKind.StaticKeyword));

                    //newMethod = newMethod.AddAttributeLists(BoxSyntaxFactory.AggressiveInliningAttributeList());

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

                    var newProp = (PropertyDeclarationSyntax) sg.PropertyDeclaration(prop,
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
    }
}
