using G9MAUIControls.Persistence.Sqlite.Queries;
using G9MAUIControls.Persistence.Sqlite;
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

public partial class SqliteRepository<T> where T : class, new()
{
    public async Task<int> InsertCoreAsync(T entity)
    {
        ApplyInsertAuditIfNeeded(entity, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(entity);
        var affectedRows = await Db.InsertAsync(entity).ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    /// <summary>
    ///     Applies insert-time audit defaults (Id generation, CreatedTime/By, …) and GUID-id
    ///     normalization to <paramref name="entity" /> WITHOUT writing it. Use this when an entity
    ///     must be inserted inside a caller-managed transaction (for example a parent + child
    ///     insert that has to be atomic via <see cref="ISqliteTableAccessor{T}.RunInTransactionAsync" />)
    ///     so the raw <c>conn.Insert(entity)</c> still produces the exact same Id/audit shape that
    ///     <see cref="InsertCoreAsync" /> would have produced. The transaction owner is responsible
    ///     for the actual <c>conn.Insert</c> call and for cache refresh.
    /// </summary>
    public void PrepareForInsert(T entity)
    {
        ApplyInsertAuditIfNeeded(entity, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(entity);
    }

    public async Task<int> InsertManyCoreAsync(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var buffered = entities as IList<T> ?? entities.ToList();
        ApplyInsertAuditIfNeeded(buffered, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(buffered);

        var affectedRows = await Db.InsertAllAsync(buffered).ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    public async Task<int> InsertBatchCoreAsync(IEnumerable<T> entities, int batchSize = 500,
        bool runInSingleTransaction = true)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);

        var buffered = entities as IList<T> ?? entities.ToList();
        if (buffered.Count == 0)
        {
            return 0;
        }

        ApplyInsertAuditIfNeeded(buffered, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(buffered);

        var total = 0;
        if (runInSingleTransaction)
        {
            await Db.RunInTransactionAsync(conn =>
            {
                foreach (var batch in Batch(buffered, batchSize))
                {
                    total += conn.InsertAll(batch, false);
                }
            }).ConfigureAwait(false);

            await RefreshCacheAfterWriteAsync(total).ConfigureAwait(false);
            return total;
        }

        foreach (var batch in Batch(buffered, batchSize))
        {
            total += await Db.InsertAllAsync(batch, false).ConfigureAwait(false);
        }

        await RefreshCacheAfterWriteAsync(total).ConfigureAwait(false);
        return total;
    }

    public async Task<int> UpdateCoreAsync(T entity)
    {
        ApplyUpdateAuditIfNeeded(entity, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(entity);
        var affectedRows = await Db.UpdateAsync(entity).ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    public async Task<int> UpdateManyCoreAsync(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var buffered = entities as IList<T> ?? entities.ToList();
        ApplyUpdateAuditIfNeeded(buffered, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(buffered);

        var affectedRows = await Db.UpdateAllAsync(buffered).ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    public async Task<int> InsertOrReplaceCoreAsync(T entity)
    {
        ApplyUpsertAuditIfNeeded(entity, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(entity);
        var affectedRows = await Db.InsertOrReplaceAsync(entity).ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    public Task<int> MergeCoreAsync(IEnumerable<T> entities, int batchSize = 500)
    {
        return MergepublicAsync(entities, Metadata.PrimaryKeyPropertyNames, batchSize);
    }

    public Task<int> MergeCoreAsync(IEnumerable<T> entities, Expression<Func<T, object>> keySelector,
        int batchSize = 500)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        var keyProperties = ExtractKeyProperties(keySelector);
        return MergepublicAsync(entities, keyProperties, batchSize);
    }

    public async Task<int> DeleteCoreAsync(T entity)
    {
        var affectedRows = await Db.DeleteAsync(entity).ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    public async Task<int> DeleteAllCoreAsync()
    {
        var affectedRows = await Db.DeleteAllAsync<T>().ConfigureAwait(false);
        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    public async Task DeleteWhereCoreAsync(Expression<Func<T, bool>> predicate)
    {
        var items = await SelectWhereCoreAsync(predicate).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return;
        }

        await Db.RunInTransactionAsync(conn =>
        {
            foreach (var item in items)
            {
                conn.Delete(item);
            }
        }).ConfigureAwait(false);

        await RefreshCacheAfterWriteAsync(items.Count).ConfigureAwait(false);
    }

    private async Task<int> MergepublicAsync(IEnumerable<T> entities, IReadOnlyCollection<string> keyProperties,
        int batchSize)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(keyProperties);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);

        var buffered = entities as IList<T> ?? entities.ToList();
        if (buffered.Count == 0)
        {
            return 0;
        }

        ApplyUpsertAuditIfNeeded(buffered, Options.Clock.Now(), SqliteEntityAuditDefaults.ResolveCurrentUserId(Options.CurrentUser));
        NormalizeGuidStringIdProperties(buffered);

        var conflictColumns = ResolveConflictColumns(keyProperties);
        var sql = GetOrBuildMergeSql(conflictColumns);
        var affectedRows = 0;

        await Db.RunInTransactionAsync(conn =>
        {
            foreach (var batch in Batch(buffered, batchSize))
            {
                foreach (var entity in batch)
                {
                    affectedRows += conn.Execute(sql, BuildMergeArgs(entity));
                }
            }
        }).ConfigureAwait(false);

        await RefreshCacheAfterWriteAsync(affectedRows).ConfigureAwait(false);
        return affectedRows;
    }

    private static object[] BuildMergeArgs(T entity)
    {
        var args = new object[Metadata.Columns.Length];
        for (var i = 0; i < Metadata.Columns.Length; i++)
        {
            args[i] = NormalizeColumnParam(Metadata.Columns[i], Metadata.Columns[i].Property.GetValue(entity));
        }

        return args;
    }

    private static string GetOrBuildMergeSql(IReadOnlyList<string> conflictColumns)
    {
        var cacheKey = string.Join("|", conflictColumns.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        return MergeSqlCache.GetOrAdd(cacheKey, _ => BuildMergeSql(conflictColumns));
    }

    private static string BuildMergeSql(IReadOnlyList<string> conflictColumns)
    {
        var table = QuoteIdentifier(Metadata.TableName);
        var insertColumnsSql = string.Join(", ", Metadata.Columns.Select(c => QuoteIdentifier(c.ColumnName)));
        var placeholdersSql = string.Join(", ", Enumerable.Repeat("?", Metadata.Columns.Length));
        var conflictSql = string.Join(", ", conflictColumns.Select(QuoteIdentifier));

        var keySet = new HashSet<string>(conflictColumns, StringComparer.OrdinalIgnoreCase);
        var updatableColumns = Metadata.Columns
            .Select(c => c.ColumnName)
            .Where(c => !keySet.Contains(c))
            .ToArray();

        if (updatableColumns.Length == 0)
        {
            return
                $"INSERT INTO {table} ({insertColumnsSql}) VALUES ({placeholdersSql}) ON CONFLICT ({conflictSql}) DO NOTHING";
        }

        var updateSql = string.Join(", ",
            updatableColumns.Select(c => $"{QuoteIdentifier(c)} = excluded.{QuoteIdentifier(c)}"));

        return
            $"INSERT INTO {table} ({insertColumnsSql}) VALUES ({placeholdersSql}) ON CONFLICT ({conflictSql}) DO UPDATE SET {updateSql}";
    }

    private static string[] ResolveConflictColumns(IEnumerable<string> keyProperties)
    {
        var keys = new List<string>();

        foreach (var key in keyProperties)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (Metadata.PropertyOrColumnToColumn.TryGetValue(key, out var columnName))
            {
                keys.Add(columnName);
                continue;
            }

            throw new ArgumentException(
                @$"Key '{key}' is not a mapped property/column on entity type {typeof(T).Name}.",
                nameof(keyProperties));
        }

        var uniqueKeys = keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueKeys.Length == 0)
        {
            throw new InvalidOperationException(
                $"No valid merge keys were resolved for entity type {typeof(T).Name}.");
        }

        return uniqueKeys;
    }

    private static string[] ExtractKeyProperties(Expression<Func<T, object>> keySelector)
    {
        var body = UnwrapConvert(keySelector.Body);

        return body switch
        {
            MemberExpression m when m.Expression == keySelector.Parameters[0] => [m.Member.Name],
            NewExpression n => ExtractFromNewExpression(n, keySelector.Parameters[0]),
            _ => throw new ArgumentException(
                @"Key selector must be a property or anonymous projection, e.g. x => x.Id or x => new { x.Code, x.RegionId }.",
                nameof(keySelector))
        };
    }

    private static string[] ExtractFromNewExpression(NewExpression expression, ParameterExpression parameter)
    {
        var keys = new List<string>(expression.Arguments.Count);

        foreach (var arg in expression.Arguments)
        {
            var member = UnwrapConvert(arg) as MemberExpression;
            if (member?.Expression != parameter)
            {
                throw new ArgumentException("All merge keys must be direct members of the entity parameter.");
            }

            keys.Add(member.Member.Name);
        }

        return keys.ToArray();
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
               } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static IEnumerable<List<T>> Batch(IList<T> source, int batchSize)
    {
        for (var i = 0; i < source.Count; i += batchSize)
        {
            var count = Math.Min(batchSize, source.Count - i);
            var batch = new List<T>(count);
            for (var j = 0; j < count; j++)
            {
                batch.Add(source[i + j]);
            }

            yield return batch;
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }

    private static void ApplyInsertAuditIfNeeded(T entity, DateTime nowUtc, string? currentUserId)
    {
        if (entity is not IG9AuditedEntity auditedEntity)
        {
            return;
        }

        SqliteEntityAuditDefaults.ApplyInsertDefaults(auditedEntity, nowUtc, currentUserId);
    }

    private static object NormalizeColumnParam(SqliteColumnBinding column, object? value)
    {
        if (column.IsGuidStringId)
        {
            value = SqliteGuidStringNormalizer.NormalizeIdLikeValue(value);
        }

        return SqliteQueryFactory.NormalizeParam(value);
    }

    private static void NormalizeGuidStringIdProperties(T entity)
    {
        foreach (var column in Metadata.Columns)
        {
            if (!column.IsGuidStringId ||
                !column.Property.CanWrite)
            {
                continue;
            }

            var value = column.Property.GetValue(entity);
            if (value is not string stringValue)
            {
                continue;
            }

            var normalized = SqliteGuidStringNormalizer.NormalizeRequiredId(stringValue);
            if (!string.Equals(stringValue, normalized, StringComparison.Ordinal))
            {
                column.Property.SetValue(entity, normalized);
            }
        }
    }

    private static void NormalizeGuidStringIdProperties(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
        {
            NormalizeGuidStringIdProperties(entity);
        }
    }

    private static void ApplyInsertAuditIfNeeded(IEnumerable<T> entities, DateTime nowUtc, string? currentUserId)
    {
        foreach (var entity in entities)
        {
            ApplyInsertAuditIfNeeded(entity, nowUtc, currentUserId);
        }
    }

    private static void ApplyUpdateAuditIfNeeded(T entity, DateTime nowUtc, string? currentUserId)
    {
        if (entity is not IG9AuditedEntity auditedEntity)
        {
            return;
        }

        SqliteEntityAuditDefaults.ApplyUpdateDefaults(auditedEntity, nowUtc, currentUserId);
    }

    private static void ApplyUpdateAuditIfNeeded(IEnumerable<T> entities, DateTime nowUtc, string? currentUserId)
    {
        foreach (var entity in entities)
        {
            ApplyUpdateAuditIfNeeded(entity, nowUtc, currentUserId);
        }
    }

    private static void ApplyUpsertAuditIfNeeded(T entity, DateTime nowUtc, string? currentUserId)
    {
        if (entity is not IG9AuditedEntity auditedEntity)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(auditedEntity.CreatedByUserId) || auditedEntity.CreatedTime == default)
        {
            SqliteEntityAuditDefaults.ApplyInsertDefaults(auditedEntity, nowUtc, currentUserId);
            return;
        }

        SqliteEntityAuditDefaults.ApplyUpdateDefaults(auditedEntity, nowUtc, currentUserId);
    }

    private static void ApplyUpsertAuditIfNeeded(IEnumerable<T> entities, DateTime nowUtc, string? currentUserId)
    {
        foreach (var entity in entities)
        {
            ApplyUpsertAuditIfNeeded(entity, nowUtc, currentUserId);
        }
    }
}
