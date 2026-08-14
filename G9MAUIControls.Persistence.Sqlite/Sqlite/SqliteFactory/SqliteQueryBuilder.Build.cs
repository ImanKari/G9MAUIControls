using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

public sealed partial class SqliteQueryBuilder<T> where T : class, new()
{
    #region Build

    /// <summary>Builds a SELECT query. Returns the SQL and ordered parameter array.</summary>
    public (string Sql, object[] Parameters) Build()
    {
        var sb = new StringBuilder(256);
        var allParams = new List<object>();

        sb.Append("SELECT ");
        if (_distinct)
        {
            sb.Append("DISTINCT ");
        }

        sb.Append(BuildSelectClause());

        sb.Append(" FROM ").Append(_rootTable);

        allParams.AddRange(_selectParams);

        foreach (var j in _joins)
        {
            sb.Append(' ').Append(j.JoinType).Append(' ').Append(j.TableName)
                .Append(" ON ").Append(j.OnSql);
            allParams.AddRange(j.OnParams);
        }

        AppendWhere(sb, allParams);
        AppendGroupBy(sb);
        AppendHaving(sb, allParams);
        AppendOrderBy(sb);
        AppendLimitOffset(sb);

        return (sb.ToString(), allParams.ToArray());
    }

    /// <summary>Builds a SELECT statement payload (SQL + parameters).</summary>
    public SqlStatement BuildStatement()
    {
        var (sql, parameters) = Build();
        return new SqlStatement(sql, parameters);
    }

    /// <summary>
    ///     Builds SELECT SQL with inlined literals for diagnostics only.
    ///     Do not execute this output directly.
    /// </summary>
    public string BuildInlinedSql()
    {
        return BuildStatement().InlinedSql;
    }

    /// <summary>Builds an UPDATE statement using <see cref="Set{TValue}" /> clauses and WHERE conditions.</summary>
    public (string Sql, object[] Parameters) BuildUpdate()
    {
        if (_setClauses.Count == 0)
        {
            throw new InvalidOperationException("No SET clauses. Use .Set(x => x.Column, value) before BuildUpdate().");
        }

        var sb = new StringBuilder(128);
        var allParams = new List<object>();

        sb.Append("UPDATE ").Append(_rootTable).Append(" SET ");
        for (var i = 0; i < _setClauses.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var columnName = ResolveColumnName(_setClauses[i].Column, typeof(T));
            sb.Append(QuoteIdentifier(columnName)).Append(" = ?");
            allParams.Add(NormalizeParamForProperty(_setClauses[i].Column, typeof(T), _setClauses[i].Value));
        }

        AppendWhere(sb, allParams);
        return (sb.ToString(), allParams.ToArray());
    }

    /// <summary>Builds an UPDATE statement payload (SQL + parameters).</summary>
    public SqlStatement BuildUpdateStatement()
    {
        var (sql, parameters) = BuildUpdate();
        return new SqlStatement(sql, parameters);
    }

    /// <summary>
    ///     Builds UPDATE SQL with inlined literals for diagnostics only.
    ///     Do not execute this output directly.
    /// </summary>
    public string BuildUpdateInlinedSql()
    {
        return BuildUpdateStatement().InlinedSql;
    }

    /// <summary>Builds a DELETE statement using WHERE conditions.</summary>
    public (string Sql, object[] Parameters) BuildDelete()
    {
        var sb = new StringBuilder(64);
        var allParams = new List<object>();

        sb.Append("DELETE FROM ").Append(_rootTable);
        AppendWhere(sb, allParams);

        return (sb.ToString(), allParams.ToArray());
    }

    /// <summary>Builds a DELETE statement payload (SQL + parameters).</summary>
    public SqlStatement BuildDeleteStatement()
    {
        var (sql, parameters) = BuildDelete();
        return new SqlStatement(sql, parameters);
    }

    /// <summary>
    ///     Builds DELETE SQL with inlined literals for diagnostics only.
    ///     Do not execute this output directly.
    /// </summary>
    public string BuildDeleteInlinedSql()
    {
        return BuildDeleteStatement().InlinedSql;
    }

    /// <summary>Returns just the SQL string (parameters are embedded as '?' placeholders).</summary>
    public override string ToString()
    {
        var (sql, _) = Build();
        return sql;
    }

    #endregion

    #region Clause Appenders

    private void AppendWhere(StringBuilder sb, List<object> allParams)
    {
        if (_whereParts.Count == 0)
        {
            return;
        }

        sb.Append(" WHERE ");
        sb.Append(_whereParts.Count == 1
            ? _whereParts[0]
            : string.Join(" AND ", _whereParts.Select(w => $"({w})")));
        allParams.AddRange(_whereParams);
    }

    private void AppendGroupBy(StringBuilder sb)
    {
        if (_groupByParts.Count > 0)
        {
            sb.Append(" GROUP BY ").Append(string.Join(", ", _groupByParts));
        }
    }

    private void AppendHaving(StringBuilder sb, List<object> allParams)
    {
        if (_havingSql == null)
        {
            return;
        }

        sb.Append(" HAVING ").Append(_havingSql);
        allParams.AddRange(_havingParams);
    }

    private void AppendOrderBy(StringBuilder sb)
    {
        if (_orderByParts.Count > 0)
        {
            sb.Append(" ORDER BY ").Append(string.Join(", ", _orderByParts));
        }
    }

    private void AppendLimitOffset(StringBuilder sb)
    {
        if (_limit.HasValue)
        {
            sb.Append(" LIMIT ").Append(_limit.Value);
        }

        if (_offset.HasValue)
        {
            sb.Append(" OFFSET ").Append(_offset.Value);
        }
    }

    private string BuildSelectClause()
    {
        if (_selectParts.Count > 0)
        {
            return string.Join(", ", _selectParts);
        }

        if (_culture == null)
        {
            return "*";
        }

        return string.Join(", ",
            GetSelectablePropertyNames(typeof(T))
                .Select(propertyName => FormatColumn(_rootTable, propertyName, typeof(T))));
    }

    #endregion
}
