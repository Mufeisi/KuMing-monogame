using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Server.Scripting.Debug
{
    internal sealed class ScriptDebugInstrumenter : CSharpSyntaxRewriter
    {
        private static readonly ExpressionSyntax StepCallee = SyntaxFactory.ParseExpression("global::Server.Scripting.Debug.ScriptDebugHook.Step");

        private readonly string _filePath;

        public ScriptDebugInstrumenter(string filePath)
        {
            _filePath = filePath ?? string.Empty;
        }

        public SyntaxTree Instrument(SyntaxTree originalTree)
        {
            if (originalTree == null) throw new ArgumentNullException(nameof(originalTree));

            var root = originalTree.GetRoot();
            var rewritten = Visit(root);

            return CSharpSyntaxTree.Create(
                (CSharpSyntaxNode)rewritten,
                (CSharpParseOptions)originalTree.Options,
                path: originalTree.FilePath,
                encoding: originalTree.Encoding);
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            if (node == null) return node;

            var newStatements = new List<StatementSyntax>(node.Statements.Count * 2);

            for (var i = 0; i < node.Statements.Count; i++)
            {
                var originalStatement = node.Statements[i];

                if (originalStatement is LocalFunctionStatementSyntax)
                {
                    newStatements.Add((StatementSyntax)Visit(originalStatement));
                    continue;
                }

                if (originalStatement is LabeledStatementSyntax originalLabeled)
                {
                    // label: <stmt> 需要保证 goto label 时能命中 Step，因此改写为：
                    // label: ;
                    // Step(...);
                    // <stmt>
                    newStatements.Add(originalLabeled.WithStatement(SyntaxFactory.EmptyStatement()));

                    if (originalLabeled.Statement != null)
                    {
                        newStatements.Add(CreateStepStatement(originalLabeled.Statement));
                        newStatements.Add((StatementSyntax)Visit(originalLabeled.Statement));
                    }
                    continue;
                }

                newStatements.Add(CreateStepStatement(originalStatement));
                var visitedStatement = (StatementSyntax)Visit(originalStatement);
                newStatements.Add(visitedStatement);
            }

            return node.WithStatements(SyntaxFactory.List(newStatements));
        }

        public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
        {
            if (node == null) return node;

            var newStatements = new List<StatementSyntax>(node.Statements.Count * 2);

            for (var i = 0; i < node.Statements.Count; i++)
            {
                var originalStatement = node.Statements[i];

                if (originalStatement is LocalFunctionStatementSyntax)
                {
                    newStatements.Add((StatementSyntax)Visit(originalStatement));
                    continue;
                }

                newStatements.Add(CreateStepStatement(originalStatement));
                newStatements.Add((StatementSyntax)Visit(originalStatement));
            }

            return node.WithStatements(SyntaxFactory.List(newStatements));
        }

        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (IfStatementSyntax)base.VisitIfStatement(node);

            var statement = InstrumentEmbeddedStatement(node.Statement, visited.Statement);

            ElseClauseSyntax elseClause = null;
            if (node.Else != null && visited.Else != null)
            {
                var elseStatement = InstrumentEmbeddedStatement(node.Else.Statement, visited.Else.Statement);
                elseClause = visited.Else.WithStatement(elseStatement);
            }

            return visited.WithStatement(statement).WithElse(elseClause);
        }

        public override SyntaxNode VisitForStatement(ForStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (ForStatementSyntax)base.VisitForStatement(node);
            return visited.WithStatement(InstrumentEmbeddedStatement(node.Statement, visited.Statement));
        }

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);
            return visited.WithStatement(InstrumentEmbeddedStatement(node.Statement, visited.Statement));
        }

        public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (WhileStatementSyntax)base.VisitWhileStatement(node);
            return visited.WithStatement(InstrumentEmbeddedStatement(node.Statement, visited.Statement));
        }

        public override SyntaxNode VisitDoStatement(DoStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (DoStatementSyntax)base.VisitDoStatement(node);
            return visited.WithStatement(InstrumentEmbeddedStatement(node.Statement, visited.Statement));
        }

        public override SyntaxNode VisitUsingStatement(UsingStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (UsingStatementSyntax)base.VisitUsingStatement(node);
            return visited.WithStatement(InstrumentEmbeddedStatement(node.Statement, visited.Statement));
        }

        public override SyntaxNode VisitLockStatement(LockStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (LockStatementSyntax)base.VisitLockStatement(node);
            return visited.WithStatement(InstrumentEmbeddedStatement(node.Statement, visited.Statement));
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node == null) return node;

            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);

            if (node.Body == null && node.ExpressionBody != null && visited.Body == null && visited.ExpressionBody != null)
            {
                var expr = node.ExpressionBody.Expression;
                var visitedExpr = visited.ExpressionBody.Expression;

                var step = CreateStepStatement(expr);

                StatementSyntax statement;

                if (visitedExpr is ThrowExpressionSyntax te)
                {
                    statement = SyntaxFactory.ThrowStatement(te.Expression);
                }
                else if (ReturnsVoid(node))
                {
                    statement = SyntaxFactory.ExpressionStatement(visitedExpr);
                }
                else
                {
                    statement = SyntaxFactory.ReturnStatement(visitedExpr);
                }

                var block = SyntaxFactory.Block(step, statement);

                return visited
                    .WithBody(block)
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default);
            }

            return visited;
        }

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (node == null) return node;

            var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node);

            if (node.Body == null && node.ExpressionBody != null && visited.Body == null && visited.ExpressionBody != null)
            {
                var expr = node.ExpressionBody.Expression;
                var visitedExpr = visited.ExpressionBody.Expression;

                var step = CreateStepStatement(expr);

                StatementSyntax statement;

                if (visitedExpr is ThrowExpressionSyntax te)
                {
                    statement = SyntaxFactory.ThrowStatement(te.Expression);
                }
                else if (ReturnsVoid(node))
                {
                    statement = SyntaxFactory.ExpressionStatement(visitedExpr);
                }
                else
                {
                    statement = SyntaxFactory.ReturnStatement(visitedExpr);
                }

                var block = SyntaxFactory.Block(step, statement);

                return visited
                    .WithBody(block)
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default);
            }

            return visited;
        }

        private static bool ReturnsVoid(MethodDeclarationSyntax node)
        {
            if (node == null) return false;
            return node.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);
        }

        private static bool ReturnsVoid(LocalFunctionStatementSyntax node)
        {
            if (node == null) return false;
            return node.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);
        }

        private StatementSyntax InstrumentEmbeddedStatement(StatementSyntax original, StatementSyntax visited)
        {
            if (original == null) return visited;

            if (original is BlockSyntax)
                return visited;

            return SyntaxFactory.Block(
                CreateStepStatement(original),
                visited);
        }

        private StatementSyntax CreateStepStatement(SyntaxNode node)
        {
            if (node == null)
                return SyntaxFactory.EmptyStatement();

            var (line, column) = GetLineAndColumn(node);

            var invocation = SyntaxFactory.InvocationExpression(
                StepCallee,
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(new[]
                    {
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(_filePath))),
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(line))),
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(column))),
                    })));

            return SyntaxFactory.ExpressionStatement(invocation);
        }

        private static (int Line, int Column) GetLineAndColumn(SyntaxNode node)
        {
            try
            {
                var span = node.GetLocation().GetLineSpan();
                return (span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}
