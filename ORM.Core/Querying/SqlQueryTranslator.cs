using System.Linq.Expressions;
using Npgsql;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Querying;

internal static class SqlQueryTranslator
{
    public static SqlQueryParts Translate(EntityMap map, Expression expression)
    {
        // Unwrap: DbSet<T>.Expression is Constant(DbSet<T>)
        var visitor = new QueryMethodVisitor(map);
        visitor.Visit(expression);
        return visitor.Build();
    }

    private sealed class QueryMethodVisitor : ExpressionVisitor
    {
        private readonly EntityMap _map;

        private Expression? _whereBody;
        private readonly List<(string Column, bool Desc)> _orderBy = new();
        private int? _take;

        private readonly List<NpgsqlParameter> _parameters = new();
        private int _paramIndex = 0;

        public QueryMethodVisitor(EntityMap map) => _map = map;

        public SqlQueryParts Build()
        {
            var whereSql = "";
            if (_whereBody is not null)
            {
                var predicateSql = new PredicateSqlVisitor(_map, AddParam).Translate(_whereBody);
                whereSql = " WHERE " + predicateSql;
            }

            var orderBySql = "";
            if (_orderBy.Count > 0)
            {
                var parts = _orderBy.Select(o => $"{Quote(o.Column)} {(o.Desc ? "DESC" : "ASC")}");
                orderBySql = " ORDER BY " + string.Join(", ", parts);
            }

            var limitSql = _take.HasValue ? $" LIMIT {_take.Value}" : "";

            return new SqlQueryParts(whereSql, orderBySql, limitSql, _parameters.ToArray());
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Expect Queryable.Where/OrderBy/ThenBy/Take
            if (node.Method.DeclaringType == typeof(Queryable))
            {
                switch (node.Method.Name)
                {
                    case "Where":
                        Visit(node.Arguments[0]);
                        var whereLambda = (LambdaExpression)StripQuotes(node.Arguments[1]);
                        _whereBody = _whereBody is null
                            ? whereLambda.Body
                            : Expression.AndAlso(_whereBody, whereLambda.Body);
                        return node;

                    case "OrderBy":
                    case "OrderByDescending":
                    case "ThenBy":
                    case "ThenByDescending":
                        Visit(node.Arguments[0]);
                        var orderLambda = (LambdaExpression)StripQuotes(node.Arguments[1]);
                        var col = ExtractMappedColumn(orderLambda.Body);
                        _orderBy.Add((col, node.Method.Name.EndsWith("Descending", StringComparison.Ordinal)));
                        return node;

                    case "Take":
                        Visit(node.Arguments[0]);
                        _take = (int)Evaluate(node.Arguments[1])!;
                        return node;
                }
            }

            return base.VisitMethodCall(node);
        }

        private string ExtractMappedColumn(Expression body)
        {
            if (body is UnaryExpression u && u.NodeType == ExpressionType.Convert)
                body = u.Operand;

            if (body is not MemberExpression m)
                throw new NotSupportedException("OrderBy supports only simple member access: x => x.Prop");

            var col = _map.Columns.SingleOrDefault(c => c.Property.Name == m.Member.Name);
            if (col is null)
                throw new InvalidOperationException($"Property '{m.Member.Name}' is not mapped.");

            return col.ColumnName;
        }

        private object? Evaluate(Expression expr)
        {
            // supports constants and captured variables
            if (expr is ConstantExpression c) return c.Value;
            var lambda = Expression.Lambda(expr);
            return lambda.Compile().DynamicInvoke();
        }

        private NpgsqlParameter AddParam(object? value)
        {
            var name = $"p{_paramIndex++}";
            var p = new NpgsqlParameter(name, value ?? DBNull.Value);
            _parameters.Add(p);
            return p;
        }

        private static Expression StripQuotes(Expression e)
        {
            while (e.NodeType == ExpressionType.Quote)
                e = ((UnaryExpression)e).Operand;
            return e;
        }

        private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
    }
}
