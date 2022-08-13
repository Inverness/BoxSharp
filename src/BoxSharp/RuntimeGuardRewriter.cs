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
        private readonly ExpressionSyntax _runtimeGuardField;

        private bool _insertedGlobal;

        private StatementSyntax? _enterMethodNode;
        private StatementSyntax? _enterStaticConstructorNode;
        private StatementSyntax? _exitStaticConstructorNode;
        private StatementSyntax? _beforeJumpNode;

        public RuntimeGuardRewriter(ExpressionSyntax runtimeGuardField)
        {
            _runtimeGuardField = runtimeGuardField;
        }

        public CompilationUnitSyntax Rewrite(CompilationUnitSyntax root)
        {
            var cu = (CompilationUnitSyntax) Visit(root);

            // The same RuntimeGuardRewriter instance will be used for all syntax trees and ensures
            // that the enter method call will be inserted before any other global statements.
            if (!_insertedGlobal)
            {
                GlobalStatementSyntax globalStatement = GlobalStatement(InsertEnterMethod((SyntaxNode?)null));

                cu = cu.WithMembers(cu.Members.Insert(0, globalStatement));

                _insertedGlobal = true;
            }

            return cu;
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

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            node = (ObjectCreationExpressionSyntax) base.VisitObjectCreationExpression(node)!;

            return MakeRgInvocation(nameof(AfterNewObject), node);
        }

        public override SyntaxNode? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            node = (ArrayCreationExpressionSyntax) base.VisitArrayCreationExpression(node)!;

            return MakeRgInvocation(nameof(AfterNewArray), node);
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

            InvocationExpressionSyntax beforeAwait = MakeRgInvocation(nameof(BeforeAwait), node.Expression);

            node = node.WithExpression(beforeAwait);

            InvocationExpressionSyntax afterAwait = MakeRgInvocation(nameof(AfterAwait), node);

            return afterAwait;
        }

        private StatementSyntax InsertBeforeJump(StatementSyntax node)
        {
            if (_beforeJumpNode == null)
                _beforeJumpNode = MakeRgInvocationStatement(nameof(BeforeJump));

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

        private BlockSyntax InsertEnterMethod(SyntaxNode? node)
        {
            if (_enterMethodNode == null)
                _enterMethodNode = MakeRgInvocationStatement(nameof(EnterMethod));

            if (node is ExpressionSyntax expr)
            {
                // This must be an expression-bodied member, so a return is required
                return Block(_enterMethodNode, ReturnStatement(expr));
            }

            if (node is StatementSyntax stmt)
            {
                return Block(_enterMethodNode, stmt);
            }

            if (node == null)
            {
                return Block(_enterMethodNode);
            }

            throw new NotImplementedException();
        }

        private BlockSyntax InsertEnterStaticConstructorMethod(SyntaxNode node)
        {
            if (_enterStaticConstructorNode == null)
                _enterStaticConstructorNode = MakeRgInvocationStatement(nameof(EnterStaticConstructor));

            if (_exitStaticConstructorNode == null)
                _exitStaticConstructorNode = MakeRgInvocationStatement(nameof(ExitStaticConstructor));

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

        private static InvocationExpressionSyntax MakeRgInvocationCore(string methodName)
        {
            return InvocationExpression(
                BoxSyntaxFactory.GlobalMemberAccess(
                    "BoxSharp",
                    "Runtime",
                    "Internal",
                    "RuntimeGuardInterface",
                    methodName));
        }

        private ExpressionStatementSyntax MakeRgInvocationStatement(string methodName)
        {
            return ExpressionStatement(
                MakeRgInvocationCore(methodName)
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(_runtimeGuardField)))));
        }

        private InvocationExpressionSyntax MakeRgInvocation(string methodName, ExpressionSyntax genericArg)
        {
            return MakeRgInvocationCore(methodName)
                    .WithArgumentList(
                        ArgumentList(
                            SeparatedList<ArgumentSyntax>(
                                NodeOrTokenList(
                                    Argument(_runtimeGuardField),
                                    Token(SyntaxKind.CommaToken),
                                    Argument(genericArg)
                                    ))));
        }
    }
}
