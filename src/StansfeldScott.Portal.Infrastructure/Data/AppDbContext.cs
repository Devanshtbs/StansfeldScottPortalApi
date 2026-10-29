using Microsoft.EntityFrameworkCore;

namespace StansfeldScott.Portal.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Entity configurations (Fluent API) go here as each module is added.
    }
}
