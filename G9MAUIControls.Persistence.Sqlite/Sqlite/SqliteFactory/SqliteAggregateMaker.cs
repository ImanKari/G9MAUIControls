namespace G9MAUIControls.Persistence.Sqlite.Queries;

/// <summary>
///     SQL aggregate / function markers for use in Select and Having expressions.
///     These methods are never executed — the expression visitor translates them to SQL.
/// </summary>
public static class SqliteAggregateMaker
{
    /// <summary>Represents <c>COUNT(*)</c> in a translated SQL expression.</summary>
    public static int Count()
    {
        throw new InvalidOperationException();
    }

    /// <summary>Represents <c>COUNT(column)</c> in a translated SQL expression.</summary>
    public static int Count<TCol>(TCol column)
    {
        throw new InvalidOperationException();
    }

    /// <summary>Represents <c>SUM(CASE WHEN condition THEN 1 ELSE 0 END)</c> in a translated SQL expression.</summary>
    public static int CountWhen(bool condition)
    {
        throw new InvalidOperationException();
    }

    /// <summary>Represents <c>SUM(column)</c> in a translated SQL expression.</summary>
    public static TCol Sum<TCol>(TCol column)
    {
        throw new InvalidOperationException();
    }

    /// <summary>Represents <c>AVG(column)</c> in a translated SQL expression.</summary>
    public static TCol Avg<TCol>(TCol column)
    {
        throw new InvalidOperationException();
    }

    /// <summary>Represents <c>MIN(column)</c> in a translated SQL expression.</summary>
    public static TCol Min<TCol>(TCol column)
    {
        throw new InvalidOperationException();
    }

    /// <summary>Represents <c>MAX(column)</c> in a translated SQL expression.</summary>
    public static TCol Max<TCol>(TCol column)
    {
        throw new InvalidOperationException();
    }

    /// <summary>Represents <c>COALESCE(primary, fallback)</c> in a translated SQL expression.</summary>
    public static TCol Coalesce<TCol>(TCol primary, TCol fallback)
    {
        throw new InvalidOperationException();
    }
}
