using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QueryGuard.ProviderTests;

/// <summary>
/// The SQLite half of the provider matrix, run on every pull request.
/// </summary>
/// <remarks>
/// SQLite is the workhorse: real relational execution with no container to start, so it can afford to
/// cover the whole surface — interception, fingerprint stability, budget evaluation, failure handling —
/// on every change. PostgreSQL then checks that none of it is accidentally SQLite-shaped.
/// </remarks>
public sealed class SqliteProviderSuiteTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;
    private readonly AsyncLocalQueryGuardSessionAccessor _accessor = new();

    public SqliteProviderSuiteTests()
    {
        _connectionString = string.Create(
            CultureInfo.InvariantCulture,
            $"Data Source=queryguard-provider-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
        Seed(context);
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task A_per_parent_query_produces_one_group_and_a_candidate_finding()
    {
        // The whole product, end to end, against a real database.
        await using var context = CreateContext();
        var session = new QueryGuardSession(
            "GET /api/companies",
            QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(3));

        using (_accessor.Activate(session))
        {
            var ids = await context.Companies.Select(company => company.Id).ToListAsync();

            foreach (var id in ids)
            {
                _ = await context.Departments.Where(department => department.CompanyId == id).ToListAsync();
            }
        }

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());

        var repeated = Assert.Single(result.Groups, group => group.Occurrences > 1);
        Assert.Equal(6, repeated.Occurrences);

        Assert.Contains(result.Findings, finding => finding.Kind == QueryFindingKind.RepeatedQueryCandidate);
        Assert.Contains(result.Findings, finding => finding.Kind == QueryFindingKind.FingerprintOccurrenceBudget);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task The_projected_equivalent_passes_the_same_policy()
    {
        await using var context = CreateContext();
        var session = new QueryGuardSession(
            "GET /api/companies",
            QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(3));

        using (_accessor.Activate(session))
        {
            _ = await context.Companies
                .Select(company => new { company.Id, Departments = company.Departments.Count })
                .ToListAsync();
        }

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Async_and_sync_execution_of_the_same_query_share_a_fingerprint()
    {
        // The two interceptor paths have to produce the same record contract, or a codebase that mixes
        // them would see one logical query as two.
        await using var context = CreateContext();
        var session = new QueryGuardSession("mixed", QueryGuardPolicy.Create("mixed"));

        using (_accessor.Activate(session))
        {
            _ = context.Companies.ToList();
            _ = await context.Companies.ToListAsync();
        }

        var completed = session.Complete();

        Assert.Equal(2, completed.Records.Count);
        Assert.Single(completed.Records.Select(record => record.Fingerprint.Id).Distinct());
    }

    [Fact]
    public async Task A_scalar_and_a_reader_over_the_same_table_are_distinct_queries()
    {
        await using var context = CreateContext();
        var session = new QueryGuardSession("mixed", QueryGuardPolicy.Create("mixed"));

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.CountAsync();
            _ = await context.Companies.ToListAsync();
        }

        var completed = session.Complete();

        Assert.Equal(2, completed.Records.Select(record => record.Fingerprint.Id).Distinct().Count());
    }

    [Fact]
    public async Task Sqlite_records_a_failure_without_replacing_the_exception()
    {
        await using var context = CreateContext();
        var session = new QueryGuardSession("failing", QueryGuardPolicy.Create("failing"));

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using (_accessor.Activate(session))
            {
                await context.Database.ExecuteSqlRawAsync("SELECT * FROM \"NoSuchTable\"");
            }
        });

        Assert.Contains("Sqlite", exception.GetType().FullName!, StringComparison.Ordinal);
        Assert.Equal(1, session.Complete().FailedCommandCount);
    }

    private ProviderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ProviderDbContext>()
            .UseSqlite(_connectionString)
            .AddInterceptors(new EntityFrameworkCore.QueryGuardCommandInterceptor(
                _accessor,
                new QueryFingerprintFactory()))
            .Options;

        return new ProviderDbContext(options);
    }

    private static void Seed(ProviderDbContext context)
    {
        for (var companyIndex = 1; companyIndex <= 6; companyIndex++)
        {
            context.Companies.Add(new Company
            {
                Id = companyIndex,
                Name = string.Create(CultureInfo.InvariantCulture, $"Company {companyIndex}"),
                City = companyIndex % 2 == 0 ? "Lyon" : "Paris",
            });

            context.Departments.Add(new Department
            {
                Id = companyIndex,
                CompanyId = companyIndex,
                Name = "Engineering",
            });
        }

        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
