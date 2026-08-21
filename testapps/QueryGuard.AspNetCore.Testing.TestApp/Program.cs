using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryGuard;
using QueryGuard.AspNetCore;
using QueryGuard.AspNetCore.Testing.TestApp;
using QueryGuard.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

builder.Services.AddSingleton(connection);
builder.Services.AddQueryGuard(options => options.DefaultPolicy = QueryGuardPolicy.Create("test-app"));

builder.Services.AddDbContext<PlainCatalogDbContext>((provider, options) =>
    options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));

builder.Services.AddDbContext<GuardedCatalogDbContext>((provider, options) =>
{
    options.UseSqlite(provider.GetRequiredService<SqliteConnection>());
    options.AddInterceptors(provider.GetRequiredService<QueryGuardCommandInterceptor>());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlainCatalogDbContext>();
    db.Database.EnsureCreated();
    db.Widgets.Add(new Widget { Name = "first" });
    db.SaveChanges();
}

app.UseRouting();
app.UseQueryGuard();

app.MapGet("/plain/widgets", async (PlainCatalogDbContext db) =>
    await db.Widgets.AsNoTracking().CountAsync().ConfigureAwait(false));

app.MapGet("/guarded/widgets", async (GuardedCatalogDbContext db) =>
    await db.Widgets.AsNoTracking().CountAsync().ConfigureAwait(false));

app.Run();

public partial class Program;
