using G9MAUIControls.Persistence.Sqlite.Queries;
using G9MAUIControls.Persistence.Sqlite;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

public partial class SqliteRepository<T> where T : class, new()
{
    public Task<int> ExecuteUpdateStatementCoreAsync(SqlStatement statement)
    {
        var auditedStatement = ApplyAutomaticUpdateAudit(statement);
        return ExecuteCoreAsync(auditedStatement.Sql, auditedStatement.Parameters);
    }

    // No longer static: it reads the injected clock and current-user provider. It was static only
    // because the source read a process-global clock and a cached static user id — precisely the coupling
    // the extension points replace.
    private SqlStatement ApplyAutomaticUpdateAudit(SqlStatement statement)
    {
        if (!typeof(IG9AuditedEntity).IsAssignableFrom(typeof(T)) ||
            string.IsNullOrWhiteSpace(statement.Sql))
        {
            return statement;
        }

        var setIndex = statement.Sql.IndexOf(" SET ", StringComparison.OrdinalIgnoreCase);
        if (setIndex < 0)
        {
            return statement;
        }

        var whereIndex = statement.Sql.IndexOf(" WHERE ", setIndex + 5, StringComparison.OrdinalIgnoreCase);
        var setStart = setIndex + 5;
        var setLength = whereIndex >= 0
            ? whereIndex - setStart
            : statement.Sql.Length - setStart;

        if (setLength <= 0)
        {
            return statement;
        }

        var setClause = statement.Sql.Substring(setStart, setLength);
        var hasUpdatedTime = setClause.Contains("UpdatedTime", StringComparison.OrdinalIgnoreCase);
        var hasUpdatedByUserId = setClause.Contains("UpdatedByUserId", StringComparison.OrdinalIgnoreCase);

        if (hasUpdatedTime && hasUpdatedByUserId)
        {
            return statement;
        }

        var insertParamIndex = CountPlaceholders(setClause);
        var parameters = new List<object>(statement.Parameters);
        var additionalAssignments = new List<string>(2);

        if (!hasUpdatedTime)
        {
            additionalAssignments.Add("[UpdatedTime] = ?");
            parameters.Insert(insertParamIndex, SqliteQueryFactory.NormalizeParam(Options.Clock.Now()));
            insertParamIndex++;
        }

        if (!hasUpdatedByUserId)
        {
            additionalAssignments.Add("[UpdatedByUserId] = ?");
            parameters.Insert(
                insertParamIndex,
                SqliteQueryFactory.NormalizeParam(SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser)));
            insertParamIndex++;
        }

        if (additionalAssignments.Count == 0)
        {
            return statement;
        }

        var sql = AppendAssignments(statement.Sql, setStart, setLength, whereIndex, additionalAssignments);
        return new SqlStatement(sql, parameters.ToArray());
    }

    private static string AppendAssignments(
        string sql,
        int setStart,
        int setLength,
        int whereIndex,
        IReadOnlyList<string> assignments)
    {
        var setEnd = setStart + setLength;
        var setSegment = sql.Substring(setStart, setLength);

        var builder = new StringBuilder(sql.Length + 80);
        builder.Append(sql.AsSpan(0, setEnd));

        if (!setSegment.TrimEnd().EndsWith(",", StringComparison.Ordinal))
        {
            builder.Append(", ");
        }

        builder.Append(string.Join(", ", assignments));

        if (whereIndex >= 0)
        {
            builder.Append(sql.AsSpan(whereIndex));
        }

        return builder.ToString();
    }

    private static int CountPlaceholders(string sql)
    {
        var count = 0;
        foreach (var ch in sql)
        {
            if (ch == '?')
            {
                count++;
            }
        }

        return count;
    }
}
