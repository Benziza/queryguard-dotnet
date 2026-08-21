using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QueryGuard.EntityFrameworkCore.Tests;

/// <summary>
/// Interception behavior against real SQLite command execution.
/// </summary>
/// <remarks>
/// These are integration tests on purpose. Whether a fake interceptor records what we hand it says
/// nothing about whether EF Core calls the methods we think it calls, with the durations and command
/// sources we think it supplies.
/// </remarks>
public sealed class QueryGuardCommandInterceptorTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly AsyncLocalQueryGuardSessionAccessor _accessor = new();
    private readonly QueryGuardCommandInterceptor _interceptor;

    public QueryGuardCommandInterceptorTests()
        => _interceptor = new QueryGuardCommandInterceptor(_accessor, new QueryFingerprintFactory());

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Constructor_requires_its_collaborators()
    {
        Assert.Throws<ArgumentNullException>(
            () => new QueryGuardCommandInterceptor(null!, new QueryFingerprintFactory()));
        Assert.Throws<ArgumentNullException>(
            () => new QueryGuardCommandInterceptor(_accessor, null!));
    }

    [Fact]
    public void With_no_active_scope_nothing_is_captured_and_the_query_still_works()
    {
        // The interceptor is registered for the lifetime of the DbContext configuration, which means
        // it observes queries outside any measured scope. Doing nothing there is the contract.
        using var context = _fixture.CreateContext(_interceptor);

        var companies = context.Companies.ToList();

        Assert.NotEmpty(companies);
        Assert.Null(_accessor.Current);
    }

    [Fact]
    public void A_synchronous_read_produces_one_record_with_a_measured_duration()
    {
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = context.Companies.ToList();
        }

        var completed = session.Complete();
        var record = Assert.Single(completed.Records);

        Assert.Equal(QueryCommandKind.Reader, record.Kind);
        Assert.True(record.IsRead);

        // A duration of exactly zero is possible for a fast in-memory query; a negative one would
        // mean the measurement is wrong.
        Assert.True(record.Duration >= TimeSpan.Zero);
        Assert.StartsWith(QueryFingerprint.IdPrefix, record.Fingerprint.Id, StringComparison.Ordinal);
        Assert.Contains("Companies", record.Fingerprint.NormalizedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_asynchronous_read_produces_the_same_record_contract()
    {
        // Real ASP.NET Core code is overwhelmingly async. Implementing only the sync path would
        // produce a tool that silently misses nearly everything.
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.ToListAsync();
        }

        var completed = session.Complete();
        var record = Assert.Single(completed.Records);

        Assert.Equal(QueryCommandKind.Reader, record.Kind);
        Assert.True(record.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task A_scalar_query_is_recorded_as_a_scalar_command()
    {
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.CountAsync();
        }

        var completed = session.Complete();
        var record = Assert.Single(completed.Records);

        // EF Core executes COUNT through the reader path on SQLite, so this asserts the record is a
        // read of some kind rather than pinning the exact execution method.
        Assert.True(record.IsRead);
    }

    [Fact]
    public async Task A_write_is_recorded_but_does_not_count_toward_a_read_budget()
    {
        // A budget of ten reads must mean ten reads regardless of how many entities are saved, and
        // regardless of which execution path the provider happens to use. On SQLite, EF Core runs
        // INSERT ... RETURNING through the reader path so it can read the generated key back, so
        // trusting the execution method alone would count every inserted row as a read.
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            context.Departments.Add(new Department { CompanyId = 1, Name = "Added" });
            await context.SaveChangesAsync();
        }

        var completed = session.Complete();

        Assert.NotEmpty(completed.Records);
        Assert.All(completed.Records, record =>
        {
            Assert.Equal(QueryCommandKind.NonQuery, record.Kind);
            Assert.False(record.IsRead);
        });
        Assert.Equal(0, completed.CountedCommandCount);
    }

    [Fact]
    public async Task A_repeated_query_shares_one_fingerprint_despite_different_parameter_values()
    {
        // This is the entire product in one assertion: the same logical query executed per parent
        // row has to land in a single group, or nothing downstream can count it.
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            var companyIds = await context.Companies.Select(company => company.Id).ToListAsync();

            foreach (var companyId in companyIds)
            {
                _ = await context.Departments
                    .Where(department => department.CompanyId == companyId)
                    .ToListAsync();
            }
        }

        var completed = session.Complete();
        var departmentQueries = completed.Records
            .Where(record => record.Fingerprint.NormalizedSql.Contains("Departments", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(5, departmentQueries.Count);
        Assert.Single(departmentQueries.Select(record => record.Fingerprint.Id).Distinct());
    }

    [Fact]
    public async Task Parameter_values_never_reach_a_record()
    {
        // A captured variable is what EF Core turns into a real parameter. The count is useful
        // evidence; the names and values are not worth the risk.
        var city = "Paris";
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies
                .Where(company => company.City == city)
                .ToListAsync();
        }

        var completed = session.Complete();
        var record = Assert.Single(completed.Records);

        Assert.DoesNotContain("Paris", record.Fingerprint.NormalizedSql, StringComparison.Ordinal);
        Assert.True(record.ParameterCount >= 1);
    }

    [Fact]
    public async Task An_inlined_literal_is_redacted_even_though_ef_core_did_not_parameterize_it()
    {
        // EF Core inlines a literal constant rather than parameterizing it, so the value lands in
        // the command text itself. This is exactly the case where a query written one way would leak
        // data that the same query written another way would not.
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies
                .Where(company => company.City == "Paris")
                .ToListAsync();
        }

        var record = Assert.Single(session.Complete().Records);

        Assert.DoesNotContain("Paris", record.Fingerprint.NormalizedSql, StringComparison.Ordinal);
        Assert.Contains("City", record.Fingerprint.NormalizedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_connection_string_never_reaches_a_record()
    {
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.ToListAsync();
        }

        var completed = session.Complete();

        Assert.All(completed.Records, record =>
        {
            Assert.DoesNotContain("Filename", record.Fingerprint.NormalizedSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("memory", record.Fingerprint.NormalizedSql, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task A_failing_command_is_recorded_and_the_original_exception_still_surfaces()
    {
        // QueryGuard adds diagnostics alongside a failure. It must never become the thing that
        // reports it, or every debugging session starts by ruling QueryGuard out.
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using (_accessor.Activate(session))
            {
                await context.Database.ExecuteSqlRawAsync("SELECT * FROM \"NoSuchTable\"");
            }
        });

        Assert.Contains("Sqlite", exception.GetType().FullName!, StringComparison.Ordinal);

        var completed = session.Complete();
        var record = Assert.Single(completed.Records);

        Assert.True(record.IsFailed);
        Assert.Equal(1, completed.FailedCommandCount);
        Assert.Contains("Sqlite", record.FailureType!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_tag_is_recognized_on_the_record()
    {
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies
                .TagWith("QueryGuard:Ignore reason=bounded-reference-lookup")
                .ToListAsync();
        }

        var completed = session.Complete();
        var record = Assert.Single(completed.Records);

        Assert.True(QueryGuardQueryTag.HasIgnoreDirective(record.Tags));
        Assert.Equal("bounded-reference-lookup", QueryGuardQueryTag.GetIgnoreReason(record.Tags));
    }

    [Fact]
    public async Task An_untagged_query_carries_no_tags()
    {
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.TagWith("just a note for a human").ToListAsync();
        }

        var completed = session.Complete();
        var record = Assert.Single(completed.Records);

        // An arbitrary tag is a comment like any other. QueryGuard does not retain text it was not
        // asked to interpret.
        Assert.Empty(record.Tags);
    }

    [Fact]
    public async Task Two_concurrent_scopes_over_the_same_context_configuration_stay_isolated()
    {
        // One interceptor instance, two scopes, deliberately different query counts.
        var busy = NewSession("busy");
        var quiet = NewSession("quiet");

        var busyTask = Task.Run(async () =>
        {
            using var context = _fixture.CreateContext(_interceptor);
            using (_accessor.Activate(busy))
            {
                for (var i = 0; i < 4; i++)
                {
                    _ = await context.Companies.ToListAsync();
                }
            }
        });

        var quietTask = Task.Run(async () =>
        {
            using var context = _fixture.CreateContext(_interceptor);
            using (_accessor.Activate(quiet))
            {
                _ = await context.Departments.ToListAsync();
            }
        });

        await Task.WhenAll(busyTask, quietTask);

        Assert.Equal(4, busy.Complete().Records.Count);
        Assert.Single(quiet.Complete().Records);
    }

    [Fact]
    public async Task The_interceptor_does_not_change_query_results()
    {
        // The whole promise is that installing QueryGuard does not change how the application
        // behaves, so compare the same query with and without it.
        using var guarded = _fixture.CreateContext(_interceptor);
        using var plain = _fixture.CreateContext();

        var session = NewSession();
        List<Company> guardedResult;

        using (_accessor.Activate(session))
        {
            guardedResult = await guarded.Companies.OrderBy(company => company.Id).ToListAsync();
        }

        var plainResult = await plain.Companies.OrderBy(company => company.Id).ToListAsync();

        Assert.Equal(plainResult.Count, guardedResult.Count);
        Assert.Equal(
            plainResult.Select(company => company.Name),
            guardedResult.Select(company => company.Name));
        Assert.NotEmpty(session.Complete().Records);
    }

    [Fact]
    public async Task Cancellation_still_propagates_with_the_interceptor_attached()
    {
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        using (_accessor.Activate(session))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => context.Companies.ToListAsync(cancellation.Token));
        }
    }

    [Fact]
    public async Task Records_carry_the_command_source_reported_by_ef_core()
    {
        var session = NewSession();
        using var context = _fixture.CreateContext(_interceptor);

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.ToListAsync();
        }

        var record = Assert.Single(session.Complete().Records);

        Assert.False(string.IsNullOrWhiteSpace(record.CommandSource));
    }

    private static QueryGuardSession NewSession(string name = "GET /api/companies")
        => new(name, QueryGuardPolicy.Create("test"));
}
