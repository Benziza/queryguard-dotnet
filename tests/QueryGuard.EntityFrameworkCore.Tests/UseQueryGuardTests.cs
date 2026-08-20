using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueryGuard.Testing;
using Xunit;

namespace QueryGuard.EntityFrameworkCore.Tests;

/// <summary>
/// The one-line wiring path, which is what a first-time user actually types.
/// </summary>
/// <remarks>
/// <para>
/// Every test here goes through <c>UseQueryGuard()</c> and <c>QueryGuardScope.Start</c> with no
/// accessor argument anywhere, because that combination is the whole point: the two defaults have to
/// already be pointing at each other. If they ever drift apart, the failure is a scope that records
/// zero commands, so these assert on real counts rather than on wiring.
/// </para>
/// </remarks>
public class UseQueryGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public UseQueryGuardTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
        db.Widgets.AddRange(
            new Widget { Name = "a" },
            new Widget { Name = "b" },
            new Widget { Name = "c" });
        db.SaveChanges();
    }

    [Fact]
    public async Task A_scope_and_UseQueryGuard_find_each_other_with_no_wiring()
    {
        await using var scope = QueryGuardScope.Start("one-line", QueryGuardPolicy.Create("p"));

        using var db = CreateContext();
        _ = await db.Widgets.AsNoTracking().ToListAsync();

        var result = await scope.CompleteAsync();

        Assert.Equal(1, result.ReadCommandCount);
    }

    [Fact]
    public async Task Repeated_queries_group_into_one_fingerprint()
    {
        await using var scope = QueryGuardScope.Start(
            "one-line",
            QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(2));

        using var db = CreateContext();
        foreach (var id in new[] { 1, 2, 3 })
        {
            _ = await db.Widgets.AsNoTracking().FirstOrDefaultAsync(widget => widget.Id == id);
        }

        var result = await scope.CompleteAsync();

        Assert.Equal(3, result.ReadCommandCount);
        var group = Assert.Single(result.Groups);
        Assert.Equal(3, group.Occurrences);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Calling_it_twice_does_not_double_count()
    {
        // The failure this prevents is nasty because the numbers stay plausible: every count comes
        // back exactly doubled, which reads as an application problem rather than a wiring one. It
        // happened in this repository's own sample, where a test factory re-registered the DbContext
        // and both configuration callbacks ran.
        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseSqlite(_connection)
            .UseQueryGuard()
            .UseQueryGuard()
            .Options;

        await using var scope = QueryGuardScope.Start("twice", QueryGuardPolicy.Create("p"));

        using var db = new WidgetContext(options);
        _ = await db.Widgets.AsNoTracking().ToListAsync();

        var result = await scope.CompleteAsync();

        Assert.Equal(1, result.ReadCommandCount);
    }

    [Fact]
    public async Task An_explicitly_constructed_interceptor_is_not_duplicated_either()
    {
        // An application that already registers the interceptor through dependency injection and
        // then adds UseQueryGuard() should not start counting twice.
        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new QueryGuardCommandInterceptor(
                AsyncLocalQueryGuardSessionAccessor.Shared,
                new QueryFingerprintFactory()))
            .UseQueryGuard()
            .Options;

        await using var scope = QueryGuardScope.Start("mixed", QueryGuardPolicy.Create("p"));

        using var db = new WidgetContext(options);
        _ = await db.Widgets.AsNoTracking().ToListAsync();

        var result = await scope.CompleteAsync();

        Assert.Equal(1, result.ReadCommandCount);
    }

    [Fact]
    public async Task Nothing_is_captured_when_no_scope_is_open()
    {
        // Wiring the interceptor is not the same as measuring. Outside a scope this has to stay
        // silent, which is what makes it safe to leave configured.
        using var db = CreateContext();
        _ = await db.Widgets.AsNoTracking().ToListAsync();

        await using var scope = QueryGuardScope.Start("after", QueryGuardPolicy.Create("p"));
        var result = await scope.CompleteAsync();

        Assert.Equal(0, result.ReadCommandCount);
    }

    [Fact]
    public async Task A_supplied_accessor_is_honoured_over_the_shared_one()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseSqlite(_connection)
            .UseQueryGuard(accessor)
            .Options;

        // The scope uses the shared accessor, the interceptor was given a different one, so the two
        // deliberately do not see each other. Asserting the miss keeps the parameter meaningful.
        await using var mismatched = QueryGuardScope.Start("mismatched", QueryGuardPolicy.Create("p"));

        using (var db = new WidgetContext(options))
        {
            _ = await db.Widgets.AsNoTracking().ToListAsync();
        }

        Assert.Equal(0, (await mismatched.CompleteAsync()).ReadCommandCount);

        await using var matched = QueryGuardScope.Start(
            "matched",
            QueryGuardPolicy.Create("p"),
            accessor: accessor);

        using (var db = new WidgetContext(options))
        {
            _ = await db.Widgets.AsNoTracking().ToListAsync();
        }

        Assert.Equal(1, (await matched.CompleteAsync()).ReadCommandCount);
    }

    [Fact]
    public void A_builder_is_required()
    {
        // Typed locals rather than casts on null: the cast is what selects the overload, and reading
        // it back as a cast makes it look decorative.
        DbContextOptionsBuilder builder = null!;
        DbContextOptionsBuilder<WidgetContext> generic = null!;

        Assert.Throws<ArgumentNullException>(() => builder.UseQueryGuard());
        Assert.Throws<ArgumentNullException>(() => generic.UseQueryGuard());
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private WidgetContext CreateContext()
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
