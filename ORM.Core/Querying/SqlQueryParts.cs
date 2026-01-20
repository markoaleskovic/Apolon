using Npgsql;

namespace ORM.Core.Querying;

internal sealed record SqlQueryParts(
    string WhereSql,
    string OrderBySql,
    string LimitSql,
    NpgsqlParameter[] Parameters
);