using G9MAUIControls.Persistence.Sqlite.Queries;
using SQLite;

namespace G9MAUIControls.Persistence.Sqlite.Tests;

/// <summary>
///     Every assertion here RUNS the generated statement against in-memory SQLite and counts rows. A
///     string comparison would have passed for most of the defects these pin down: the SQL was
///     well-formed, it just selected the wrong rows (or, for the keyword table names, did not parse).
/// </summary>
/// <remarks>
///     Fixture, three rows in a table deliberately named <c>Order</c>:
///     <code>
///         Id   ParentId  A  B  C   Code
///         r    NULL      1  2  3   x
///         c1   r         1  1  10  y
///         c2   q         5  5  1   NULL
///     </code>
///     xunit builds a new instance per test, so each test gets its own database.
/// </remarks>
public sealed class SqlGenerationTests : IDisposable
{
    private const int InValueCountBeyondSqliteVariableLimit = 40_000; // SQLITE_MAX_VARIABLE_NUMBER is 32,766

    private readonly SQLiteConnection _db = new(":memory:");

    public SqlGenerationTests()
    {
        _db.CreateTable<Node>();
        _db.CreateTable<Other>();
        _db.Insert(new Other { NodeId = "r" });
        _db.Insert(new Node { Id = "r", ParentId = null, A = 1, B = 2, C = 3, Code = "x" });
        _db.Insert(new Node { Id = "c1", ParentId = "r", A = 1, B = 1, C = 10, Code = "y" });
        _db.Insert(new Node { Id = "c2", ParentId = "q", A = 5, B = 5, C = 1, Code = null });
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private static SqliteSelectQueryBuilder<Node> Query()
    {
        return SqliteQueryFactory.Select<Node>().SelectAll();
    }

    private int Rows(SqlStatement statement)
    {
        return _db.Query<Node>(statement.Sql, statement.Parameters).Count;
    }

    // ── NULL-aware equality ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void CapturedNullEqualsMatchesTheNullRow()
    {
        string? parent = null;
        Assert.Equal(1, Rows(Query().Where(x => x.ParentId == parent).BuildStatement()));
    }

    [Fact]
    public void CapturedNullNotEqualsMatchesTheNonNullRows()
    {
        string? parent = null;
        Assert.Equal(2, Rows(Query().Where(x => x.ParentId != parent).BuildStatement()));
    }

    [Fact]
    public void CapturedValueEqualsIsUnchanged()
    {
        var parent = "r";
        Assert.Equal(1, Rows(Query().Where(x => x.ParentId == parent).BuildStatement()));
    }

    [Fact]
    public void CapturedValueNotEqualsKeepsTheNullRow()
    {
        var parent = "r";
        Assert.Equal(2, Rows(Query().Where(x => x.ParentId != parent).BuildStatement()));
    }

    [Fact]
    public void LiteralNullEqualsIsUnchanged()
    {
        Assert.Equal(1, Rows(Query().Where(x => x.ParentId == null).BuildStatement()));
    }

    [Fact]
    public void NotNullAndNotEmptyIdiom()
    {
        Assert.Equal(2, Rows(Query().Where(x => x.Code != null && x.Code != string.Empty).BuildStatement()));
    }

    [Fact]
    public void IdColumnEqualsCapturedNullMatchesNothing()
    {
        string? id = null;
        Assert.Equal(0, Rows(Query().Where(x => x.Id == id).BuildStatement()));
    }

    [Fact]
    public void IdColumnNotEqualsValue()
    {
        var id = "c1";
        Assert.Equal(2, Rows(Query().Where(x => x.Id != id).BuildStatement()));
    }

    [Fact]
    public void ColumnToColumnComparisonIsUntouched()
    {
        Assert.Equal(2, Rows(Query().Where(x => x.A == x.B).BuildStatement()));
    }

    // ── Arithmetic precedence ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ParenthesisedArithmeticKeepsItsParentheses()
    {
        Assert.Equal(1, Rows(Query().Where(x => (x.A + x.B) * x.C == 9).BuildStatement()));
    }

    [Fact]
    public void UnparenthesisedArithmeticKeepsNaturalPrecedence()
    {
        Assert.Equal(1, Rows(Query().Where(x => x.A + x.B * x.C == 7).BuildStatement()));
    }

    // ── Contains → IN ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ArrayContains()
    {
        // Under C# 14 this binds to the ReadOnlySpan overload of Contains, not Enumerable.Contains.
        var codes = new[] { "x", "y" };
        Assert.Equal(2, Rows(Query().Where(x => codes.Contains(x.Code)).BuildStatement()));
    }

    [Fact]
    public void NegatedArrayContains()
    {
        var codes = new[] { "x", "y" };
        Assert.Equal(0, Rows(Query().Where(x => !codes.Contains(x.Code)).BuildStatement()));
    }

    [Fact]
    public void ArrayContainsOnTheIdColumn()
    {
        var ids = new[] { "r", "c1" };
        Assert.Equal(2, Rows(Query().Where(x => ids.Contains(x.Id)).BuildStatement()));
    }

    [Fact]
    public void EnumerableCastContainsStillWorks()
    {
        var codes = new[] { "x", "y" };
        Assert.Equal(2,
            Rows(Query().Where(x => ((IEnumerable<string>)codes).Contains(x.Code)).BuildStatement()));
    }

    [Fact]
    public void ListContainsIsUnchanged()
    {
        var codes = new List<string> { "x" };
        Assert.Equal(1, Rows(Query().Where(x => codes.Contains(x.Code!)).BuildStatement()));
    }

    // ── Paging ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BareOffsetEmitsLimitMinusOne()
    {
        var statement = SqliteQueryFactory.Select<Node>().SelectAll().OrderBy(x => x.Id).Offset(1).BuildStatement();

        // SQLite has no OFFSET without LIMIT; "LIMIT -1" is its spelling of "no limit".
        Assert.Contains("LIMIT -1", statement.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Rows(statement));
    }

    [Fact]
    public void LimitWithOffsetIsUnchanged()
    {
        var statement = SqliteQueryFactory.Select<Node>().SelectAll().OrderBy(x => x.Id).Limit(1).Offset(1)
            .BuildStatement();

        Assert.Equal(1, Rows(statement));
    }

    // ── IN lists past SQLITE_MAX_VARIABLE_NUMBER ────────────────────────────────────────────────

    [Fact]
    public void InListBeyondTheVariableLimitGoesThroughJsonEach()
    {
        var codes = Enumerable.Range(0, InValueCountBeyondSqliteVariableLimit).Select(i => "v" + i).Append("y")
            .ToArray();
        var statement = Query().Where(x => codes.Contains(x.Code)).BuildStatement();

        Assert.Contains("json_each", statement.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Rows(statement));
    }

    [Fact]
    public void NotInListBeyondTheVariableLimit()
    {
        var codes = Enumerable.Range(0, InValueCountBeyondSqliteVariableLimit).Select(i => "v" + i).Append("y")
            .ToArray();

        Assert.Equal(1, Rows(Query().Where(x => !codes.Contains(x.Code)).BuildStatement()));
    }

    [Fact]
    public void IntegerInListBeyondTheVariableLimit()
    {
        var values = Enumerable.Range(0, InValueCountBeyondSqliteVariableLimit).ToList();

        Assert.Equal(3, Rows(Query().Where(x => values.Contains(x.C)).BuildStatement()));
    }

    [Fact]
    public void ModerateInListStaysOnPlaceholders()
    {
        var codes = Enumerable.Range(0, 1000).Select(i => "v" + i).Append("x").ToArray();
        var statement = Query().Where(x => codes.Contains(x.Code)).BuildStatement();

        Assert.DoesNotContain("json_each", statement.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(codes.Length, statement.Parameters.Length);
        Assert.Equal(1, Rows(statement));
    }

    // ── Keyword table names ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void JoinBetweenTwoKeywordNamedTablesParsesAndRuns()
    {
        // "Order" JOIN "Group": only parses if both table names are quoted.
        var statement = SqliteQueryFactory.Select<Node>().SelectAll()
            .InnerJoin<Node, Other>((n, o) => n.Id == o.NodeId)
            .Where<Other>(o => o.NodeId != null)
            .BuildStatement();

        Assert.Equal(1, Rows(statement));
    }

    // ── UPDATE / DELETE must say which rows ─────────────────────────────────────────────────────

    [Fact]
    public void UpdateWithoutWhereIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqliteQueryFactory.Update<Node>().Set(x => x.A, 1).BuildStatement());
    }

    [Fact]
    public void DeleteWithoutWhereIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => SqliteQueryFactory.Delete<Node>().BuildStatement());
    }

    [Fact]
    public void AllRowsCombinedWithWhereIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqliteQueryFactory.Delete<Node>().AllRows().Where(x => x.A == 1).BuildStatement());
    }

    [Fact]
    public void UpdateWithNullAwareWhereAffectsTheExpectedRows()
    {
        var parent = "r";
        var statement = SqliteQueryFactory.Update<Node>().Set(x => x.A, 9).Where(x => x.ParentId != parent)
            .BuildStatement();

        Assert.Equal(2, _db.Execute(statement.Sql, statement.Parameters));
    }

    [Fact]
    public void DeleteAllRowsDeletesEveryRow()
    {
        var statement = SqliteQueryFactory.Delete<Node>().AllRows().BuildStatement();

        Assert.Equal(3, _db.Execute(statement.Sql, statement.Parameters));
    }

    // ── Fixture entities ────────────────────────────────────────────────────────────────────────

    [Table("Order")] // an SQL keyword on purpose
    public sealed class Node
    {
        [PrimaryKey] public string Id { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public string? Code { get; set; }
    }

    [Table("Group")] // an SQL keyword on purpose
    public sealed class Other
    {
        [PrimaryKey] public string NodeId { get; set; } = string.Empty;
    }
}
