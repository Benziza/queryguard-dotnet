using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueryGuard;
using QueryGuard.EntityFrameworkCore;
using QueryGuard.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SampleApplication.Diagnostics;

/// <summary>
/// A failure that names the call site, not just the SQL.
/// </summary>
/// <remarks>
/// <para>
/// "This endpoint has a repeated query" leaves the developer to go find it. "Line 87 has a repeated
/// query" does not. That difference is most of the value of a diagnostics tool, and it is why a scope
/// captures the origin by default even though the interceptor on a request path does not — see
/// <c>docs/decisions/0007-stack-trace-policy.md</c>.
/// </para>
/// <para>
/// These tests go through a real EF Core context against SQLite rather than constructing a result by
/// hand, because the thing being tested is whether a stack captured inside the interceptor survives
/// filtering with the caller's own frame still in it.
/// </para>
/// </remarks>
public sealed class OriginReportingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ITestOutputHelper _output;

    public OriginReportingTests(ITestOutputHelper output)
    {
        _output = output;

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Widgets.AddRange(
            new Widget { Name = "a" },
            new Widget { Name = "b" },
            new Widget { Name = "c" });
        db.SaveChanges();
    }

    [Fact]
    public async Task A_failure_names_the_method_that_ran_the_repeated_query()
    {
        var result = await RunTheLoopAsync();

        var message = QueryGuardAssert.Describe(result);
        _output.WriteLine(message);

        Assert.Contains("origin:", message, StringComparison.Ordinal);

        // The method that actually contains the loop, not the interceptor and not xunit.
        Assert.Contains(nameof(RunTheLoopAsync), message, StringComparison.Ordinal);
        Assert.Contains(nameof(OriginReportingTests), message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_framework_frames_reach_the_message()
    {
        // The filter is what makes the origin line useful. Without it the nearest frame is EF Core
        // internals, which is true and useless.
        var message = QueryGuardAssert.Describe(await RunTheLoopAsync());

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", message, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryGuard.EntityFrameworkCore.QueryGuardCommandInterceptor", message, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Runtime", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_origin_can_be_turned_off()
    {
        var result = await RunTheLoopAsync(captureOrigin: false);

        Assert.DoesNotContain("origin:", QueryGuardAssert.Describe(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_supplied_redactor_still_decides_for_itself()
    {
        // Passing a redactor means "use exactly this configuration". The convenience default must not
        // quietly override it, or a caller who disabled capture on purpose gets it back.
        await using var scope = QueryGuardScope.Start(
            "explicit-redactor",
            QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(1),
            redactor: new QueryGuardRedactor());

        await QueryEachWidgetAsync();

        Assert.DoesNotContain("origin:", QueryGuardAssert.Describe(await scope.CompleteAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_one_origin_is_captured_per_fingerprint()
    {
        // Bounded by design: one trace per distinct query, not one per execution. Fifty traces for
        // fifty executions of the same query would be fifty copies of the same answer.
        var result = await RunTheLoopAsync();

        var group = Assert.Single(result.Groups, candidate => candidate.Occurrences > 1);
        var withStack = group.Samples.Count(sample => sample.StackTrace is not null);

        Assert.Equal(1, withStack);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Runs the same query once per row — the shape QueryGuard exists to find.
    /// </summary>
    /// <remarks>
    /// A named method rather than an inline loop, so the assertions above can look for it by name and
    /// prove the origin points at application code rather than at a lambda in a test framework.
    /// </remarks>
    private async Task<QueryGuardResult> RunTheLoopAsync(bool captureOrigin = true)
    {
        await using var scope = QueryGuardScope.Start(
            "GET /widgets",
            QueryGuardPolicy.Create("widgets").WithMaxOccurrencesPerFingerprint(1),
            captureOrigin: captureOrigin);

        await QueryEachWidgetAsync();

        return await scope.CompleteAsync();
    }

    private async Task QueryEachWidgetAsync()
    {
        using var db = NewContext();

        foreach (var id in new[] { 1, 2, 3 })
        {
            _ = await db.Widgets.AsNoTracking().FirstOrDefaultAsync(widget => widget.Id == id);
        }
    }

    private WidgetContext NewContext()
        => new(new DbContextOptionsBuilder<WidgetContext>()
            .UseSqlite(_connection)
            .UseQueryGuard()
            .Options);

    internal sealed class WidgetContext : DbContext
    {
        public WidgetContext(DbContextOptions<WidgetContext> options)
            : base(options)
        {
        }

        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            modelBuilder.Entity<Widget>(entity =>
            {
                entity.HasKey(widget => widget.Id);
                entity.Property(widget => widget.Name).IsRequired();
            });
        }
    }

    internal sealed class Widget
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
