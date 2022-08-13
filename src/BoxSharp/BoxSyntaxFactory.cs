using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BoxSharp
{
    internal static class BoxSyntaxFactory
    {
        private static SyntaxGenerator? s_syntaxGenerator;

        internal static SyntaxGenerator SyntaxGenerator
        {
            get
            {
                if (s_syntaxGenerator == null)
                {
                    const string sgTypeName = "Microsoft.CodeAnalysis.CSharp.CodeGeneration.CSharpSyntaxGenerator, Microsoft.CodeAnalysis.CSharp.Workspaces";

                    Type sgType = Type.GetType(sgTypeName, true, false);

                    s_syntaxGenerator = (SyntaxGenerator) Activator.CreateInstance(sgType, true);
                }

                return s_syntaxGenerator;
            }
        }

        internal static string MakeSpecialName(string name)
        {
            return $"\u003C{name}\u003E";
        }

        internal static MemberAccessExpressionSyntax MakeGlobalMemberAccess(params string[] names)
        {
            Debug.Assert(names != null && names.Length > 1);

            //AliasQualifiedNameSyntax aqn = AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)),
            //                                                  IdentifierName(names![0]));

            MemberAccessExpressionSyntax expr = MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                AliasQualifiedName(
                    IdentifierName(Token(SyntaxKind.GlobalKeyword)),
                    IdentifierName(names![0])),
                IdentifierName(names[1]));

            for (int i = 2; i < names.Length; i++)
            {
                expr = MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    expr,
                    IdentifierName(names[i]));
            }

            return expr;
        }

        internal static MemberAccessExpressionSyntax MakeMemberAccess(ExpressionSyntax expr, params string[] names)
        {
            Debug.Assert(names != null && names.Length > 0);

            foreach (string name in names!)
            {
                expr = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                              expr,
                                              IdentifierName(name));
            }

            return (MemberAccessExpressionSyntax) expr;
        }

        internal static MemberAccessExpressionSyntax MakeRgAccess(string scriptClassName, string genClassName)
        {
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    AliasQualifiedName(
                        IdentifierName(Token(SyntaxKind.GlobalKeyword)),
                        IdentifierName(scriptClassName)),
                    IdentifierName(genClassName)),
                IdentifierName(ScriptClassGenerator.RuntimeGuardFieldName));
        }

        internal static MemberAccessExpressionSyntax MakeRgAccess(string genClassName)
        {
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                AliasQualifiedName(
                    IdentifierName(Token(SyntaxKind.GlobalKeyword)),
                    IdentifierName(genClassName)),
                IdentifierName(ScriptClassGenerator.RuntimeGuardFieldName));
        }
    }
}
