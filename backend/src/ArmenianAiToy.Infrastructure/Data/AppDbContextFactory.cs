using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArmenianAiToy.Infrastructure.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create an
/// <see cref="AppDbContext"/> for migration generation. Bypasses
/// <c>Program.cs</c> entirely — important because that startup path
/// validates <c>Jwt:Key</c> and <c>OpenAI:ApiKey</c> and throws if they
/// are missing. Neither secret is needed to design a schema, so EF
/// tooling (including CI) can run without any secret being provisioned.
///
/// The connection string here is only used by the tooling to parse the
/// provider (SQLite); at runtime the real connection string still comes
/// from <c>appsettings.json</c> via <c>DependencyInjection.AddInfrastructure</c>.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=armenian_ai_toy.db")
            .Options;
        return new AppDbContext(options);
    }
}
