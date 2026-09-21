using G9MAUIControls.Persistence.Sqlite.Queries;
using SQLite;
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

// Read part
public partial class SqliteRepository<T> where T : class, new()
{
    public async Task<T?> FindByIdCoreAsync(int id)
    {
        try
        {
            return await Db.FindAsync<T>(id).ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (IsTableNotFoundError(ex))
        {
            return null;
        }
    }

    public async Task<T?> FindByPredicateCoreAsync(Expression<Func<T, bool>> predicate)
    {
        var statement = SqliteQueryFactory
            .Select<T>()
            .SelectAll()
            .Where(predicate)
            .Limit(1)
            .BuildStatement();

        var rows = await QueryCoreAsync(statement.Sql, statement.Parameters).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<List<T>> SelectAllCoreAsync()
    {
        try
        {
            return await Db.Table<T>().ToListAsync().ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (IsTableNotFoundError(ex))
        {
            return [];
        }
    }

    public async Task<List<T>> SelectWhereCoreAsync(Expression<Func<T, bool>> predicate)
    {
        var statement = SqliteQueryFactory
            .Select<T>()
            .SelectAll()
            .Where(predicate)
            .BuildStatement();

        return await QueryCoreAsync(statement.Sql, statement.Parameters).ConfigureAwait(false);
    }

    public async Task<int> CountAllCoreAsync()
    {
        try
        {
            return await Db.Table<T>().CountAsync().ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (IsTableNotFoundError(ex))
        {
            return 0;
        }
    }

    public async Task<int> CountWhereCoreAsync(Expression<Func<T, bool>> predicate)
    {
        var statement = SqliteQueryFactory
            .Select<T>()
            .SelectCount()
            .Where(predicate)
            .BuildStatement();

        return await ScalarCoreAsync<int>(statement.Sql, statement.Parameters).ConfigureAwait(false);
    }

    public async Task<bool> AnyCoreAsync(Expression<Func<T, bool>> predicate)
    {
        // EXISTS over "SELECT 1 … LIMIT 1" stops at the first matching row. This was
        // SELECT COUNT(*) … LIMIT 1, where the LIMIT applies to the single aggregate ROW, not to the scan:
        // every matching row was still counted just to learn whether there was one. Same answer, and the
        // inner statement is the builder's own, so the predicate is translated and bound exactly as before.
        var statement = SqliteQueryFactory
            .Select<T>()
            .SelectRaw("1")
            .Where(predicate)
            .Limit(1)
            .BuildStatement();

        return await ScalarCoreAsync<int>($"SELECT EXISTS({statement.Sql})", statement.Parameters)
            .ConfigureAwait(false) > 0;
    }
}
