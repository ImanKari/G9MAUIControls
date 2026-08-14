using SQLite;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

public partial class SqliteRepository<T> where T : class, new()
{
    public async Task<List<T>> QueryCoreAsync(string sql, params object[] args)
    {
        try
        {
            return await Db.QueryAsync<T>(sql, args).ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (IsTableNotFoundError(ex))
        {
            return [];
        }
    }

    public async Task<List<TResult>> QueryCoreAsync<TResult>(string sql, params object[] args)
        where TResult : class, new()
    {
        try
        {
            return await Db.QueryAsync<TResult>(sql, args).ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (IsTableNotFoundError(ex))
        {
            return [];
        }
    }

    public async Task<TResult> ScalarCoreAsync<TResult>(string sql, params object[] args)
    {
        try
        {
            return await Db.ExecuteScalarAsync<TResult>(sql, args).ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (IsTableNotFoundError(ex))
        {
            return default!;
        }
    }

    public async Task<int> ExecuteCoreAsync(string sql, params object[] args)
    {
        var affectedRows = await Db.ExecuteAsync(sql, args).ConfigureAwait(false);
        if (IsMutationSql(sql))
        {
            await RefreshCacheAfterWriteAsync(affectedRows, true).ConfigureAwait(false);
        }

        return affectedRows;
    }

    public async Task RunInTransactionCoreAsync(Action<SQLiteConnection> action)
    {
        await Db.RunInTransactionAsync(action).ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(0, true).ConfigureAwait(false);
    }

    private static bool IsMutationSql(string sql)
    {
        var keyword = ReadLeadingSqlKeyword(sql);
        return keyword is "INSERT" or "UPDATE" or "DELETE" or "REPLACE";
    }

    private static string? ReadLeadingSqlKeyword(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        var index = 0;
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
                continue;
            }

            if (sql[index] == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (sql[index] == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/'))
                {
                    index++;
                }

                index = Math.Min(index + 2, sql.Length);
                continue;
            }

            break;
        }

        if (index >= sql.Length || !char.IsLetter(sql[index]))
        {
            return null;
        }

        var start = index;
        while (index < sql.Length && char.IsLetter(sql[index]))
        {
            index++;
        }

        return sql[start..index].ToUpperInvariant();
    }
}