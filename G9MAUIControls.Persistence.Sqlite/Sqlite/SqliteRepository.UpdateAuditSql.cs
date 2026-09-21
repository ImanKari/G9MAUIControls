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

        // Whole-identifier, assignment-target matches. A substring test read `[LastUpdatedTimeOnServer] = ?`
        // as "UpdatedTime is already set" and skipped the stamp, leaving the row looking unmodified.
        var hasUpdatedTime = AssignsColumn(setClause, "UpdatedTime");
        var hasUpdatedByUserId = AssignsColumn(setClause, "UpdatedByUserId");

        // Resolved once, up front. With nobody signed in there is nothing to stamp, and the column must be
        // left ALONE — this used to append `[UpdatedByUserId] = NULL`, erasing who last touched the row
        // every time a background job ran a partial update. The entity-object path
        // (SqliteEntityAuditDefaults.ApplyUpdateDefaults) has always skipped a null user; now both agree.
        var currentUserId = hasUpdatedByUserId
            ? null
            : SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser);
        var stampUpdatedByUserId = !hasUpdatedByUserId && !string.IsNullOrWhiteSpace(currentUserId);

        if (hasUpdatedTime && !stampUpdatedByUserId)
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

        if (stampUpdatedByUserId)
        {
            additionalAssignments.Add("[UpdatedByUserId] = ?");
            parameters.Insert(insertParamIndex, SqliteQueryFactory.NormalizeParam(currentUserId));
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

    /// <summary>
    ///     Whether <paramref name="setClause" /> assigns to <paramref name="column" /> — the whole identifier,
    ///     bare or quoted (<c>[x]</c>, <c>"x"</c>, <c>`x`</c>), followed by <c>=</c>. A column that merely
    ///     CONTAINS the name, or one that appears on the right-hand side of another assignment, is not a match.
    /// </summary>
    private static bool AssignsColumn(string setClause, string column)
    {
        var index = 0;
        while ((index = setClause.IndexOf(column, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = index + column.Length;
            var startsClean = index == 0 || !IsIdentifierChar(setClause[index - 1]);
            var endsClean = end >= setClause.Length || !IsIdentifierChar(setClause[end]);

            if (startsClean && endsClean)
            {
                var next = end;
                while (next < setClause.Length &&
                       (setClause[next] is ']' or '"' or '`' || char.IsWhiteSpace(setClause[next])))
                {
                    next++;
                }

                if (next < setClause.Length && setClause[next] == '=')
                {
                    return true;
                }
            }

            index = end;
        }

        return false;

        static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
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
