using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static BoxSharp.Runtime.Internal.RuntimeGuardInterface;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BoxSharp
{
    /// <summary>
    /// Rewrites syntax trees to inject calls to <see cref="BoxSharp.Runtime.Internal.RuntimeGuardInterface"/>
    /// </summary>
    internal class RuntimeGuardRewriter : CSharpSyntaxRewriter
    {
        private readonly int _gid;

        private bool _handledFirstGlobal;

        private readonly Compilation _compilation;

        private StatementSyntax? _enterMethodNode;
        private StatementSyntax? _enterStaticConstructorNode;
        private StatementSyntax? _exitStaticConstructorNode;
        private StatementSyntax? _beforeJumpNode;

        public RuntimeGuardRewriter(int gid, Compilation compilation)
        {
            if (gid <= 0)
                throw new ArgumentOutOfRangeException(nameof(gid));
            _gid = gid;
            _compilation = compilation;
        }

        public SyntaxNode Rewrite(SyntaxNode root)
        {
            return Visit(root);
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            node = (ConstructorDeclarationSyntax) base.VisitConstructorDeclaration(node)!;

            if (IsStaticConstructor(node))
            {
                if (node.ExpressionBody != null)
                {
                    return node.WithExpressionBody(null).WithBody(InsertEnterStaticConstructorMethod(node.ExpressionBody));
                }
                else
                {
                    return node.WithBody(InsertEnterStaticConstructorMethod(node.Body!));
                }
            }

            return InsertEnterMethod(node);
        }

        public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            node = (AccessorDeclarationSyntax) base.VisitAccessorDeclaration(node)!;

            return InsertEnterMethod(node);
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            node = (MethodDeclarationSyntax) base.VisitMethodDeclaration(node)!;

            return InsertEnterMethod(node);
        }

        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            node = (SimpleLambdaExpressionSyntax) base.VisitSimpleLambdaExpression(node)!;

            return InsertEnterMethod(node);
        }

        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            node = (ParenthesizedLambdaExpressionSyntax) base.VisitParenthesizedLambdaExpression(node)!;

            return InsertEnterMethod(node);
        }

        public override SyntaxNode? VisitGlobalStatement(GlobalStatementSyntax node)
        {
            node = (GlobalStatementSyntax) base.VisitGlobalStatement(node)!;

            if (_handledFirstGlobal)
                return node;

            _handledFirstGlobal = true;
            return node.WithStatement(InsertEnterMethod(node.Statement));
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            node = (ObjectCreationExpressionSyntax) base.VisitObjectCreationExpression(node)!;

            return MakeRgInvocation(nameof(AfterNewObject), _gid, node);
        }

        public override SyntaxNode? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            node = (ArrayCreationExpressionSyntax) base.VisitArrayCreationExpression(node)!;

            return MakeRgInvocation(nameof(AfterNewArray), _gid, node);
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            node = (DoStatementSyntax) base.VisitDoStatement(node)!;

            return node.WithStatement(InsertBeforeJump(node.Statement));
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            node = (WhileStatementSyntax) base.VisitWhileStatement(node)!;

            return node.WithStatement(InsertBeforeJump(node.Statement));
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            node = (ForStatementSyntax) base.VisitForStatement(node)!;

            return node.WithStatement(InsertBeforeJump(node.Statement));
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            node = (ForEachStatementSyntax) base.VisitForEachStatement(node)!;

            return node.WithStatement(InsertBeforeJump(node.Statement));
        }

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            node = (ForEachVariableStatementSyntax) base.VisitForEachVariableStatement(node)!;

            return node.WithStatement(InsertBeforeJump(node.Statement));
        }

        public override SyntaxNode? VisitLabeledStatement(LabeledStatementSyntax node)
        {
            node = (LabeledStatementSyntax) base.VisitLabeledStatement(node)!;

            return node.WithStatement(InsertBeforeJump(node.Statement));
        }

        public override SyntaxNode? VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            node = (AwaitExpressionSyntax) base.VisitAwaitExpression(node)!;

            InvocationExpressionSyntax beforeAwait = MakeRgInvocation(nameof(BeforeAwait), _gid, node.Expression);

            node = node.WithExpression(beforeAwait);

            InvocationExpressionSyntax afterAwait = MakeRgInvocation(nameof(AfterAwait), _gid, node);

            return afterAwait;
        }

        // private SemanticModel GetSemanticModel()
        // {
        //     if (_semanticModel == null)
        //     {
        //         _semanticModel = _compilation.GetSemanticModel(_tree);
        //     }

        //     return _semanticModel;
        // }

        private StatementSyntax InsertBeforeJump(StatementSyntax node)
        {
            if (_beforeJumpNode == null)
                _beforeJumpNode = MakeRgInvocationStatement(nameof(BeforeJump), _gid);

            // InitSemanticModel();

            // ControlFlowAnalysis? flowAnalysis = _semanticModel.AnalyzeControlFlow(node);

            // if (flowAnalysis?.Succeeded == true)
            // {

            // }

            if (node is BlockSyntax block)
            {
                SyntaxList<StatementSyntax> newStatements = block.Statements.Insert(0, _beforeJumpNode);

                return block.WithStatements(newStatements);
            }
            else
            {
                return Block(_beforeJumpNode, node);
            }
        }

        private BaseMethodDeclarationSyntax InsertEnterMethod(BaseMethodDeclarationSyntax node)
        {
            if (node.ExpressionBody != null)
            {
                return node.WithExpressionBody(null).WithBody(InsertEnterMethod(node.ExpressionBody));
            }
            else
            {
                return node.WithBody(InsertEnterMethod(node.Body!));
            }
        }

        private AccessorDeclarationSyntax InsertEnterMethod(AccessorDeclarationSyntax node)
        {
            if (node.ExpressionBody != null)
            {
                return node.WithExpressionBody(null).WithBody(InsertEnterMethod(node.ExpressionBody));
            }
            else
            {
                return node.WithBody(InsertEnterMethod(node.Body!));
            }
        }

        private AnonymousFunctionExpressionSyntax InsertEnterMethod(AnonymousFunctionExpressionSyntax node)
        {
            if (node.ExpressionBody != null)
            {
                return node.WithExpressionBody(null).WithBody(InsertEnterMethod(node.ExpressionBody));
            }
            else
            {
                return node.WithBody(InsertEnterMethod(node.Body!));
            }
        }

        private BlockSyntax InsertEnterMethod(SyntaxNode node)
        {
            if (_enterMethodNode == null)
                _enterMethodNode = MakeRgInvocationStatement(nameof(EnterMethod), _gid);

            if (node is ExpressionSyntax expr)
            {
                // This must be an expression-bodied member, so a return is required
                return Block(_enterMethodNode, ReturnStatement(expr));
            }

            if (node is StatementSyntax stmt)
            {
                return Block(_enterMethodNode, stmt);
            }

            throw new NotImplementedException();
        }

        private BlockSyntax InsertEnterStaticConstructorMethod(SyntaxNode node)
        {
            if (_enterStaticConstructorNode == null)
                _enterStaticConstructorNode = MakeRgInvocationStatement(nameof(EnterStaticConstructor), _gid);

            if (_exitStaticConstructorNode == null)
                _exitStaticConstructorNode = MakeRgInvocationStatement(nameof(ExitStaticConstructor), _gid);

            if (node is ExpressionSyntax expr)
            {
                return Block(_enterStaticConstructorNode, ExpressionStatement(expr), _exitStaticConstructorNode);
            }

            if (node is StatementSyntax stmt)
            {
                return Block(_enterStaticConstructorNode, stmt, _exitStaticConstructorNode);
            }

            throw new NotImplementedException();
        }

        private static bool IsStaticConstructor(ConstructorDeclarationSyntax node)
        {
            return node.Modifiers.Any(SyntaxKind.StaticKeyword);
        }

        // private static SyntaxNode? GetMethodBody(SyntaxNode node)
        // {
        //     if (node is BaseMethodDeclarationSyntax m)
        //     {
        //         if (m.Body != null)
        //             return m.Body;
        //         if (m.ExpressionBody != null)
        //             return m.ExpressionBody;
        //         throw new NotImplementedException();
        //     }
        //     if (node is AccessorDeclarationSyntax a)
        //     {
        //         if (a.Body != null)
        //             return a.Body;
        //         if (a.ExpressionBody != null)
        //             return a.ExpressionBody;
        //         throw new NotImplementedException();
        //     }
        //     return null;
        // }

        // private static bool IsMethodBody(SyntaxNode node)
        // {
        //     if (node is BaseMethodDeclarationSyntax || node is AccessorDeclarationSyntax)
        //         return true;

        //     return node.Parent!.Kind() switch
        //     {
        //         SyntaxKind.MethodDeclaration or
        //         SyntaxKind.GetAccessorDeclaration or
        //         SyntaxKind.SetAccessorDeclaration or
        //         SyntaxKind.AddAccessorDeclaration or
        //         SyntaxKind.RemoveAccessorDeclaration or
        //         SyntaxKind.AnonymousMethodExpression or
        //         SyntaxKind.SimpleLambdaExpression or
        //         SyntaxKind.ParenthesizedLambdaExpression or
        //         SyntaxKind.ConstructorDeclaration => true,
        //         _ => false,
        //     };
        // }

        private static InvocationExpressionSyntax MakeRgInvocationCore(string methodName)
        {
            return InvocationExpression(
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
                    IdentifierName(methodName)));
        }

        //private static InvocationExpressionSyntax MakeRgInvocationCore(string methodName, TypeSyntax genericType)
        //{
        //    return InvocationExpression(
        //        MemberAccessExpression(
        //            SyntaxKind.SimpleMemberAccessExpression,
        //            MemberAccessExpression(
        //                SyntaxKind.SimpleMemberAccessExpression,
        //                MemberAccessExpression(
        //                    SyntaxKind.SimpleMemberAccessExpression,
        //                    MemberAccessExpression(
        //                        SyntaxKind.SimpleMemberAccessExpression,
        //                        AliasQualifiedName(
        //                            IdentifierName(
        //                                Token(SyntaxKind.GlobalKeyword)),
        //                            IdentifierName("BoxSharp")),
        //                        IdentifierName("Runtime")),
        //                    IdentifierName("Internal")),
        //                IdentifierName("RuntimeGuardInterface")),
        //            GenericName(
        //                Identifier(methodName),
        //                TypeArgumentList(SingletonSeparatedList(genericType)))));
        //}

        //internal static ExpressionStatementSyntax MakeRuntimeGuardCall(string methodName)
        //{
        //    return ExpressionStatement(MakeRuntimeGuardInvocation(methodName));
        //}

        private static ExpressionStatementSyntax MakeRgInvocationStatement(string methodName, int gid)
        {
            return ExpressionStatement(
                MakeRgInvocationCore(methodName)
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(
                                LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    Literal(gid)))))));
        }

        private static InvocationExpressionSyntax MakeRgInvocation(
            string methodName,
            int gid,
            ExpressionSyntax genericArg)
        {
            return MakeRgInvocationCore(methodName)
                    .WithArgumentList(
                        ArgumentList(
                            SeparatedList<ArgumentSyntax>(
                                NodeOrTokenList(
                                    Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(gid))),
                                    Token(SyntaxKind.CommaToken),
                                    Argument(genericArg)
                                    ))));
        }

        //internal static InvocationExpressionSyntax MakeRuntimeGuardCall(
        //    string methodName,
        //    TypeSyntax genericType,
        //    int gid,
        //    ExpressionSyntax genericArg)
        //{
        //    return MakeRuntimeGuardInvocation(methodName, genericType)
        //        .WithArgumentList(
        //            ArgumentList(
        //                SeparatedList<ArgumentSyntax>(
        //                    NodeOrTokenList(
        //                        Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(gid))),
        //                        Token(SyntaxKind.CommaToken),
        //                        Argument(genericArg)
        //                        ))));
        //}
    }
}
