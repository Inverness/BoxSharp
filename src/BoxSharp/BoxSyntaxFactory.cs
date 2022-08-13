using System;
using System.Diagnostics;
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
                    // The normal way of getting a SyntaxGenerator instance involves using a bunch of workspace related types
                    // that we don't need. Instead we'll just construct an instance of the internal CSharpSyntaxGenerator directly.
                    const string sgTypeName = "Microsoft.CodeAnalysis.CSharp.CodeGeneration.CSharpSyntaxGenerator, Microsoft.CodeAnalysis.CSharp.Workspaces";

                    Type sgType = Type.GetType(sgTypeName, true, false);

                    s_syntaxGenerator = (SyntaxGenerator) Activator.CreateInstance(sgType, true);
                }

                return s_syntaxGenerator;
            }
        }

        /// <summary>
        /// Makes a special name that cannot be referenced by parsed C# code.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        internal static string MakeSpecialName(string name)
        {
            return $"\u003C{name}\u003E";
        }

        internal static AliasQualifiedNameSyntax GlobalAliasedName(string name)
        {
            return AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)), IdentifierName(name));
        }

        internal static MemberAccessExpressionSyntax GlobalMemberAccess(params string[] names)
        {
            Debug.Assert(names != null && names.Length > 1);

            //AliasQualifiedNameSyntax aqn = AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)),
            //                                                  IdentifierName(names![0]));

            MemberAccessExpressionSyntax expr = MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                GlobalAliasedName(names![0]),
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

        internal static MemberAccessExpressionSyntax MemberAccess(ExpressionSyntax expr, params string[] names)
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

        internal static NameSyntax GlobalQualifiedName(params string[] names)
        {
            Debug.Assert(names != null && names.Length > 0);

            NameSyntax q = GlobalAliasedName(names![0]);

            for (int i = 1; i < names.Length; i++)
            {
                q = QualifiedName(q, IdentifierName(names[i]));
            }

            return q;
        }

        internal static AttributeSyntax AggressiveInliningAttribute()
        {
            NameSyntax attrName = GlobalQualifiedName("System", "Runtime", "CompilerServices", "MethodImpl");

            MemberAccessExpressionSyntax optionsArg = GlobalMemberAccess("System", "Runtime", "CompilerServices", "MethodImplOptions", "AggressiveInlining");

            return Attribute(attrName, AttributeArgumentList(SingletonSeparatedList(AttributeArgument(optionsArg))));
        }

        internal static AttributeListSyntax AggressiveInliningAttributeList()
        {
            return AttributeList(SingletonSeparatedList(AggressiveInliningAttribute()));
        }
    }
}
