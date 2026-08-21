namespace QueryGuard.Tests;

/// <summary>
/// SQL shaped the way each provider shapes it, over a synthetic Companies/Departments schema.
/// </summary>
/// <remarks>
/// <para>
/// These fixtures exist because the normalizer's job is provider-specific in exactly one way that
/// matters: parameter syntax and identifier quoting differ, and the normalizer has to see through
/// both. Testing against a single provider's SQL would let a dialect assumption hide.
/// </para>
/// <para>
/// Every statement here is invented over an invented schema. No SQL, table name, or column name in
/// this repository comes from a real application. When a fixture changes, the diff <em>is</em> the
/// review: never regenerate one without reading what changed.
/// </para>
/// </remarks>
internal static class ProviderSqlFixtures
{
    /// <summary>
    /// SQLite: double-quoted identifiers, <c>@__name_0</c> parameters.
    /// </summary>
    public const string SqliteDepartmentsByCompany = """
        SELECT "d"."Id", "d"."CompanyId", "d"."Name"
        FROM "Departments" AS "d"
        WHERE "d"."CompanyId" = @__companyId_0
        """;

    /// <summary>
    /// The same SQLite query on a later execution, where the closure parameter index moved on.
    /// </summary>
    /// <remarks>
    /// This is the case the whole feature depends on. Without parameter normalization these two
    /// statements would be different fingerprints, and a per-parent query in a loop would look like
    /// N distinct queries instead of one repeated N times.
    /// </remarks>
    public const string SqliteDepartmentsByCompanyDifferentParameterIndex = """
        SELECT "d"."Id", "d"."CompanyId", "d"."Name"
        FROM "Departments" AS "d"
        WHERE "d"."CompanyId" = @__companyId_7
        """;

    /// <summary>
    /// The same query as EF Core sometimes emits it: one line, different indentation.
    /// </summary>
    public const string SqliteDepartmentsByCompanySingleLine =
        "SELECT \"d\".\"Id\", \"d\".\"CompanyId\", \"d\".\"Name\" FROM \"Departments\" AS \"d\" WHERE \"d\".\"CompanyId\" = @__companyId_0";

    /// <summary>
    /// PostgreSQL through Npgsql: double-quoted identifiers, <c>$1</c> positional parameters, and a
    /// <c>::</c> cast that must not be mistaken for a named parameter.
    /// </summary>
    public const string PostgresDepartmentsByCompany = """
        SELECT d."Id", d."CompanyId", d."Name"
        FROM "Departments" AS d
        WHERE d."CompanyId" = $1::integer
        """;

    /// <summary>
    /// The same PostgreSQL query with a different positional index.
    /// </summary>
    public const string PostgresDepartmentsByCompanyDifferentPosition = """
        SELECT d."Id", d."CompanyId", d."Name"
        FROM "Departments" AS d
        WHERE d."CompanyId" = $3::integer
        """;

    /// <summary>
    /// SQL Server: bracketed identifiers, <c>@__name_0</c> parameters, and the batch prologue EF Core
    /// prepends.
    /// </summary>
    public const string SqlServerDepartmentsByCompany = """
        SET NOCOUNT ON;
        SELECT [d].[Id], [d].[CompanyId], [d].[Name]
        FROM [Departments] AS [d]
        WHERE [d].[CompanyId] = @__companyId_0;
        """;

    /// <summary>
    /// The same SQL Server query without the prologue and with a different parameter index.
    /// </summary>
    public const string SqlServerDepartmentsByCompanyBareStatement = """
        SELECT [d].[Id], [d].[CompanyId], [d].[Name]
        FROM [Departments] AS [d]
        WHERE [d].[CompanyId] = @__companyId_12
        """;

    /// <summary>
    /// MySQL through Pomelo: backtick identifiers.
    /// </summary>
    public const string MySqlDepartmentsByCompany = """
        SELECT `d`.`Id`, `d`.`CompanyId`, `d`.`Name`
        FROM `Departments` AS `d`
        WHERE `d`.`CompanyId` = @__companyId_0
        """;

    /// <summary>
    /// A different query over the same table. Grouping this with the ones above would be the worse
    /// kind of failure: a report pointing at SQL that is not the problem.
    /// </summary>
    public const string SqliteDepartmentsByName = """
        SELECT "d"."Id", "d"."CompanyId", "d"."Name"
        FROM "Departments" AS "d"
        WHERE "d"."Name" = @__name_0
        """;

    /// <summary>
    /// A query whose column list is reordered. It is a genuinely different statement and must not be
    /// merged: the normalizer never reorders or sorts tokens.
    /// </summary>
    public const string SqliteDepartmentsReorderedColumns = """
        SELECT "d"."Name", "d"."CompanyId", "d"."Id"
        FROM "Departments" AS "d"
        WHERE "d"."CompanyId" = @__companyId_0
        """;

    /// <summary>
    /// A query carrying a QueryGuard ignore directive through <c>TagWith</c>.
    /// </summary>
    public const string SqliteTaggedIgnore = """
        -- QueryGuard:Ignore reason=bounded-reference-lookup

        SELECT "c"."Id", "c"."Name"
        FROM "Companies" AS "c"
        """;

    /// <summary>
    /// A query carrying an ordinary human tag, which is a comment like any other.
    /// </summary>
    public const string SqliteTaggedHumanNote = """
        -- reviewed by the platform team

        SELECT "c"."Id", "c"."Name"
        FROM "Companies" AS "c"
        """;
}
