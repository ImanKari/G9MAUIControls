using G9MAUIControls.Persistence.Sqlite.Queries;
using SQLite;
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

public interface ISqliteSelectAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    SqliteSelectQueryBuilder<T> Compose();
    Task<T?> ByIdAsync(int id);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    Task<List<T>> AllAsync();
    Task<List<T>> WhereAsync(Expression<Func<T, bool>> predicate);
    Task<int> CountAsync();
    Task<int> CountAsync(Expression<Func<T, bool>> predicate);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);
    Task<List<T>> QueryAsync(Func<SqliteSelectQueryBuilder<T>, SqliteSelectQueryBuilder<T>> compose);

    Task<List<TResult>> QueryAsync<TResult>(
        Func<SqliteSelectQueryBuilder<T>, SqliteSelectQueryBuilder<T>> compose) where TResult : class, new();

    Task<List<T>> QueryAsync(SqlStatement statement);
    Task<List<TResult>> QueryAsync<TResult>(SqlStatement statement) where TResult : class, new();
}

public interface ISqliteInsertAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    Task<int> OneAsync(T entity);
    Task<int> ManyAsync(IEnumerable<T> entities);
    Task<int> BatchAsync(IEnumerable<T> entities, int batchSize = 500, bool runInSingleTransaction = true);
    Task<int> OrReplaceAsync(T entity);
    Task<int> MergeAsync(IEnumerable<T> entities, int batchSize = 500);
    Task<int> MergeAsync(IEnumerable<T> entities, Expression<Func<T, object>> keySelector, int batchSize = 500);
}

public interface ISqliteUpdateAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    SqliteUpdateQueryBuilder<T> Compose();
    Task<int> OneAsync(T entity);
    Task<int> ManyAsync(IEnumerable<T> entities);
    Task<int> SetAuditUserIdsAsync(Expression<Func<T, bool>> predicate, string? auditUserId);
    Task<int> ExecuteAsync(Func<SqliteUpdateQueryBuilder<T>, SqliteUpdateQueryBuilder<T>> compose);
    Task<int> ExecuteAsync(SqlStatement statement);
}

public interface ISqliteDeleteAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    SqliteDeleteQueryBuilder<T> Compose();
    Task<int> EntityAsync(T entity);
    Task<int> AllAsync();
    Task WhereAsync(Expression<Func<T, bool>> predicate);
    Task<int> ExecuteAsync(Func<SqliteDeleteQueryBuilder<T>, SqliteDeleteQueryBuilder<T>> compose);
    Task<int> ExecuteAsync(SqlStatement statement);
}

public interface ISqliteTableAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    string Name { get; }
    Task<bool> ExistsAsync();
    Task<bool> HasColumnAsync(string columnName);
    Task<bool> HasColumnAsync(Expression<Func<T, object>> selector);
    Task<List<T>> QueryAsync(string sql, params object[] args);
    Task<List<T>> QueryAsync(SqlStatement statement);
    Task<List<TResult>> QueryAsync<TResult>(string sql, params object[] args) where TResult : class, new();
    Task<List<TResult>> QueryAsync<TResult>(SqlStatement statement) where TResult : class, new();
    Task<TResult> ScalarAsync<TResult>(string sql, params object[] args);
    Task<int> ExecuteAsync(string sql, params object[] args);
    Task<int> ExecuteAsync(SqlStatement statement);
    Task RunInTransactionAsync(Action<SQLiteConnection> action);
}

internal sealed class SqliteSelectAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(SqliteRepository<T> repository, ISqliteTableAccessor<T> table)
    : ISqliteSelectAccessor<T> where T : class, new()
{
    public SqliteSelectQueryBuilder<T> Compose()
    {
        return SqliteQueryFactory.Select<T>();
    }

    public Task<T?> ByIdAsync(int id)
    {
        return repository.FindByIdCoreAsync(id);
    }

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        return repository.FindByPredicateCoreAsync(predicate);
    }

    public Task<List<T>> AllAsync()
    {
        return repository.SelectAllCoreAsync();
    }

    public Task<List<T>> WhereAsync(Expression<Func<T, bool>> predicate)
    {
        return repository.SelectWhereCoreAsync(predicate);
    }

    public Task<int> CountAsync()
    {
        return repository.CountAllCoreAsync();
    }

    public Task<int> CountAsync(Expression<Func<T, bool>> predicate)
    {
        return repository.CountWhereCoreAsync(predicate);
    }

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate)
    {
        return repository.AnyCoreAsync(predicate);
    }

    public Task<List<T>> QueryAsync(Func<SqliteSelectQueryBuilder<T>, SqliteSelectQueryBuilder<T>> compose)
    {
        return table.QueryAsync(BuildStatement(compose));
    }

    public Task<List<TResult>> QueryAsync<TResult>(
        Func<SqliteSelectQueryBuilder<T>, SqliteSelectQueryBuilder<T>> compose) where TResult : class, new()
    {
        return table.QueryAsync<TResult>(BuildStatement(compose));
    }

    public Task<List<T>> QueryAsync(SqlStatement statement)
    {
        return table.QueryAsync(statement);
    }

    public Task<List<TResult>> QueryAsync<TResult>(SqlStatement statement) where TResult : class, new()
    {
        return table.QueryAsync<TResult>(statement);
    }

    private static SqlStatement BuildStatement(Func<SqliteSelectQueryBuilder<T>, SqliteSelectQueryBuilder<T>> compose)
    {
        ArgumentNullException.ThrowIfNull(compose);

        var builder = compose(SqliteQueryFactory.Select<T>());
        ArgumentNullException.ThrowIfNull(builder);
        return builder.BuildStatement();
    }
}

internal sealed class SqliteInsertAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(SqliteRepository<T> repository) : ISqliteInsertAccessor<T>
    where T : class, new()
{
    public Task<int> OneAsync(T entity)
    {
        return repository.InsertCoreAsync(entity);
    }

    public Task<int> ManyAsync(IEnumerable<T> entities)
    {
        return repository.InsertManyCoreAsync(entities);
    }

    public Task<int> BatchAsync(IEnumerable<T> entities, int batchSize = 500, bool runInSingleTransaction = true)
    {
        return repository.InsertBatchCoreAsync(entities, batchSize, runInSingleTransaction);
    }

    public Task<int> OrReplaceAsync(T entity)
    {
        return repository.InsertOrReplaceCoreAsync(entity);
    }

    public Task<int> MergeAsync(IEnumerable<T> entities, int batchSize = 500)
    {
        return repository.MergeCoreAsync(entities, batchSize);
    }

    public Task<int> MergeAsync(IEnumerable<T> entities, Expression<Func<T, object>> keySelector, int batchSize = 500)
    {
        return repository.MergeCoreAsync(entities, keySelector, batchSize);
    }
}

internal sealed class SqliteUpdateAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(SqliteRepository<T> repository)
    : ISqliteUpdateAccessor<T> where T : class, new()
{
    public SqliteUpdateQueryBuilder<T> Compose()
    {
        return SqliteQueryFactory.Update<T>();
    }

    public Task<int> OneAsync(T entity)
    {
        return repository.UpdateCoreAsync(entity);
    }

    public Task<int> ManyAsync(IEnumerable<T> entities)
    {
        return repository.UpdateManyCoreAsync(entities);
    }

    public Task<int> SetAuditUserIdsAsync(Expression<Func<T, bool>> predicate, string? auditUserId)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var createdBySelector = TryBuildStringPropertySelector("CreatedByUserId");
        var updatedBySelector = TryBuildStringPropertySelector("UpdatedByUserId");
        if (createdBySelector is null &&
            updatedBySelector is null)
        {
            return Task.FromResult(0);
        }

        var builder = SqliteQueryFactory.Update<T>();
        if (createdBySelector is not null)
        {
            builder = builder.Set(createdBySelector, auditUserId);
        }

        if (updatedBySelector is not null)
        {
            builder = builder.Set(updatedBySelector, auditUserId);
        }

        var statement = builder
            .Where(predicate)
            .BuildStatement();

        return repository.ExecuteUpdateStatementCoreAsync(statement);
    }

    public Task<int> ExecuteAsync(Func<SqliteUpdateQueryBuilder<T>, SqliteUpdateQueryBuilder<T>> compose)
    {
        return repository.ExecuteUpdateStatementCoreAsync(BuildStatement(compose));
    }

    public Task<int> ExecuteAsync(SqlStatement statement)
    {
        return repository.ExecuteUpdateStatementCoreAsync(statement);
    }

    private static Expression<Func<T, string?>>? TryBuildStringPropertySelector(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        if (property is null ||
            !property.CanRead ||
            property.PropertyType != typeof(string))
        {
            return null;
        }

        var parameter = Expression.Parameter(typeof(T), "entity");
        var body = Expression.Property(parameter, property);
        return Expression.Lambda<Func<T, string?>>(body, parameter);
    }

    private static SqlStatement BuildStatement(Func<SqliteUpdateQueryBuilder<T>, SqliteUpdateQueryBuilder<T>> compose)
    {
        ArgumentNullException.ThrowIfNull(compose);

        var builder = compose(SqliteQueryFactory.Update<T>());
        ArgumentNullException.ThrowIfNull(builder);
        return builder.BuildStatement();
    }
}

internal sealed class SqliteDeleteAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(SqliteRepository<T> repository, ISqliteTableAccessor<T> table)
    : ISqliteDeleteAccessor<T> where T : class, new()
{
    public SqliteDeleteQueryBuilder<T> Compose()
    {
        return SqliteQueryFactory.Delete<T>();
    }

    public Task<int> EntityAsync(T entity)
    {
        return repository.DeleteCoreAsync(entity);
    }

    public Task<int> AllAsync()
    {
        return repository.DeleteAllCoreAsync();
    }

    public Task WhereAsync(Expression<Func<T, bool>> predicate)
    {
        return repository.DeleteWhereCoreAsync(predicate);
    }

    public Task<int> ExecuteAsync(Func<SqliteDeleteQueryBuilder<T>, SqliteDeleteQueryBuilder<T>> compose)
    {
        return table.ExecuteAsync(BuildStatement(compose));
    }

    public Task<int> ExecuteAsync(SqlStatement statement)
    {
        return table.ExecuteAsync(statement);
    }

    private static SqlStatement BuildStatement(Func<SqliteDeleteQueryBuilder<T>, SqliteDeleteQueryBuilder<T>> compose)
    {
        ArgumentNullException.ThrowIfNull(compose);

        var builder = compose(SqliteQueryFactory.Delete<T>());
        ArgumentNullException.ThrowIfNull(builder);
        return builder.BuildStatement();
    }
}

internal sealed class SqliteTableAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(SqliteRepository<T> repository) : ISqliteTableAccessor<T>
    where T : class, new()
{
    private static readonly string TableName = SqliteQueryFactory.GetTableName(typeof(T));
    private static readonly SqliteTableMetadata<T> Metadata = SqliteRepositoryMetadata<T>.Value;

    public string Name => TableName;

    public async Task<bool> ExistsAsync()
    {
        var count = await repository.ScalarCoreAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?",
                TableName)
            .ConfigureAwait(false);
        return count > 0;
    }

    public async Task<bool> HasColumnAsync(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        var count = await repository.ScalarCoreAsync<long>(
                $"SELECT COUNT(*) FROM pragma_table_info('{TableName}') WHERE lower(name) = lower(?)",
                columnName)
            .ConfigureAwait(false);

        return count > 0;
    }

    public Task<bool> HasColumnAsync(Expression<Func<T, object>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var expression = selector.Body;
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        if (expression is not MemberExpression memberExpression)
        {
            throw new ArgumentException("Selector must be a direct property expression.", nameof(selector));
        }

        var propertyName = memberExpression.Member.Name;
        if (!Metadata.PropertyOrColumnToColumn.TryGetValue(propertyName, out var columnName))
        {
            throw new ArgumentException($"Property '{propertyName}' is not mapped on entity '{typeof(T).Name}'.",
                nameof(selector));
        }

        return HasColumnAsync(columnName);
    }

    public Task<List<T>> QueryAsync(string sql, params object[] args)
    {
        return repository.QueryCoreAsync(sql, args);
    }

    public Task<List<T>> QueryAsync(SqlStatement statement)
    {
        return QueryAsync(statement.Sql, statement.Parameters);
    }

    public Task<List<TResult>> QueryAsync<TResult>(string sql, params object[] args) where TResult : class, new()
    {
        return repository.QueryCoreAsync<TResult>(sql, args);
    }

    public Task<List<TResult>> QueryAsync<TResult>(SqlStatement statement) where TResult : class, new()
    {
        return QueryAsync<TResult>(statement.Sql, statement.Parameters);
    }

    public Task<TResult> ScalarAsync<TResult>(string sql, params object[] args)
    {
        return repository.ScalarCoreAsync<TResult>(sql, args);
    }

    public Task<int> ExecuteAsync(string sql, params object[] args)
    {
        return repository.ExecuteCoreAsync(sql, args);
    }

    public Task<int> ExecuteAsync(SqlStatement statement)
    {
        return ExecuteAsync(statement.Sql, statement.Parameters);
    }

    public Task RunInTransactionAsync(Action<SQLiteConnection> action)
    {
        return repository.RunInTransactionCoreAsync(action);
    }
}