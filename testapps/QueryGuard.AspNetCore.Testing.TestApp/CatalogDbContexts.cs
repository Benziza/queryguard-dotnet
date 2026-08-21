using Microsoft.EntityFrameworkCore;

namespace QueryGuard.AspNetCore.Testing.TestApp;

public sealed class PlainCatalogDbContext : DbContext
{
    public PlainCatalogDbContext(DbContextOptions<PlainCatalogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();
}

public sealed class GuardedCatalogDbContext : DbContext
{
    public GuardedCatalogDbContext(DbContextOptions<GuardedCatalogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();
}

public sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
