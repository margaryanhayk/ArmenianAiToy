using ArmenianAiToy.Api.Security;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #071 — dev/prod data discipline for the SQLite connection string.
/// Development falls back to the dev default; any non-Development environment
/// fails fast when unset or still on the dev default, so prod can never
/// silently run on the dev-named file.
/// </summary>
public class DatabaseConnectionStringTests
{
    [Fact]
    public void Development_Unset_FallsBackToDevDefault()
    {
        Assert.Equal(DatabaseConnectionString.DevDefault,
            DatabaseConnectionString.Resolve(isDevelopment: true, configured: null));
        Assert.Equal(DatabaseConnectionString.DevDefault,
            DatabaseConnectionString.Resolve(isDevelopment: true, configured: "   "));
    }

    [Fact]
    public void Development_Explicit_IsTrimmedAndReturned()
    {
        Assert.Equal("Data Source=dev2.db",
            DatabaseConnectionString.Resolve(isDevelopment: true, configured: "  Data Source=dev2.db  "));
    }

    [Fact]
    public void NonDevelopment_Explicit_IsReturned()
    {
        const string prod = "Data Source=/var/lib/areg/prod.db";
        Assert.Equal(prod,
            DatabaseConnectionString.Resolve(isDevelopment: false, configured: prod));
    }

    [Fact]
    public void NonDevelopment_Unset_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionString.Resolve(isDevelopment: false, configured: null));
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionString.Resolve(isDevelopment: false, configured: ""));
    }

    [Fact]
    public void NonDevelopment_DevDefault_Throws_EvenIfExplicit()
    {
        // Tell-tale copy-paste of the dev string into a prod overlay.
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionString.Resolve(isDevelopment: false, configured: DatabaseConnectionString.DevDefault));
        // Case-insensitive so "DATA SOURCE=..." is caught too.
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionString.Resolve(isDevelopment: false, configured: "DATA SOURCE=armenian_ai_toy.db"));
    }
}
