namespace ArmenianAiToy.Api.Security;

/// <summary>
/// #071 — dev/prod data discipline for the SQLite connection string. The
/// shipped default (<c>Data Source=armenian_ai_toy.db</c>) is fine for a dev
/// laptop, but a production instance that forgets to override it would
/// silently write to a dev-named file — and could pick up a stray dev DB that
/// happens to sit on the host, mixing real and junk data.
///
/// <para>
/// This resolves the effective connection string and FAILS FAST in any
/// non-Development environment when nothing was set explicitly, OR when the
/// configured value is the dev default (a tell-tale copy-paste). Development
/// falls back to the dev default so a fresh clone just runs. The base
/// <c>appsettings.json</c> ships an EMPTY <c>Database:ConnectionString</c> and
/// <c>appsettings.Development.json</c> carries the dev file name, so the two
/// environments are distinct by construction.
/// </para>
///
/// Pure function — no config/host access — so it is unit-testable;
/// <c>Program.cs</c> calls it before <c>AddInfrastructure</c> and writes the
/// resolved value back into configuration.
/// </summary>
public static class DatabaseConnectionString
{
    public const string DevDefault = "Data Source=armenian_ai_toy.db";

    public static string Resolve(bool isDevelopment, string? configured)
    {
        var trimmed = configured?.Trim();

        if (!string.IsNullOrEmpty(trimmed))
        {
            // A non-Development environment must not run on the dev-named file,
            // even if it was set explicitly (a copy-paste from a dev overlay).
            if (!isDevelopment
                && string.Equals(trimmed, DevDefault, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Database:ConnectionString is the development default " +
                    $"(\"{DevDefault}\") in a non-Development environment. Set a " +
                    "distinct, environment-specific connection string (e.g. via the " +
                    "Database__ConnectionString environment variable) so production " +
                    "never shares or reuses the dev database file.");
            }
            return trimmed;
        }

        // Unset / empty.
        if (isDevelopment)
            return DevDefault;

        throw new InvalidOperationException(
            "Database:ConnectionString must be set explicitly in a non-Development " +
            "environment (e.g. via the Database__ConnectionString environment " +
            "variable). The development default is intentionally NOT applied in " +
            "production so a deploy cannot silently run on a dev-named SQLite file.");
    }
}
