using System.Linq.Expressions;
using Npgsql;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Querying;

internal sealed class PredicateSqlVisitor : ExpressionVisitor
{
    private readonly EntityMap _map;
    private readonly Func<object?, NpgsqlParameter> _addParam;
    private readonly Stack<string> _stack = new();

    public PredicateSqlVisitor(EntityMap map, Func<object?, NpgsqlParameter> addParam)
    {
        _map = map;
        _addParam = addParam;
    }

    public string Translate(Expression expr)
    {
        Visit(expr);
        if (_stack.Count != 1) throw new InvalidOperationException("Invalid predicate translation.");
        return _stack.Pop();
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        Visit(node.Left);
        Visit(node.Right);

        var right = _stack.Pop();
        var left = _stack.Pop();

        var op = node.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "AND",
            ExpressionType.OrElse => "OR",
            _ => throw new NotSupportedException($"Binary operator not supported: {node.NodeType}")
        };

        _stack.Push($"({left} {op} {right})");
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ParameterExpression)
        {
            var col = _map.Columns.SingleOrDefault(c => c.Property.Name == node.Member.Name);
            if (col is null)
                throw new InvalidOperationException($"Property '{node.Member.Name}' is not mapped.");
            _stack.Push(Quote(col.ColumnName));
            return node;
        }

        //evaluate to constant
        var value = Evaluate(node);
        PushParam(value);
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        PushParam(node.Value);
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Convert)
        {
            Visit(node.Operand);
            return node;
        }

        if (node.NodeType == ExpressionType.Not)
        {
            Visit(node.Operand);
            var inner = _stack.Pop();
            _stack.Push($"(NOT {inner})");
            return node;
        }

        return base.VisitUnary(node);
    }

    private void PushParam(object? value)
    {
        var p = _addParam(value);
        _stack.Push("@" + p.ParameterName);
    }

    private static object? Evaluate(Expression expr)
    {
        if (expr is ConstantExpression c) return c.Value;
        var lambda = Expression.Lambda(expr);
        return lambda.Compile().DynamicInvoke();
    }

    private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
}
