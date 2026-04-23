using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Infrastructure.Notifications;

/// <summary>
/// Startup-time precondition enforced when the dormant-parent warn
/// pass is enabled. <see cref="LoggingNotifier"/> "delivers" to stdout
/// and never reaches real parents — if the worker marked parents as
/// warned based on LoggingNotifier's always-true return, dormant
/// accounts would accumulate silently with the system believing they'd
/// been contacted. Fail-fast at process start is the right tier for
/// this guard: an operator who sets <c>Dormancy:Parent:WarnAfterDays</c>
/// to a positive value without flipping <c>Notifications:Transport</c>
/// to <c>smtp</c> sees the error once, fixes one config, and moves on.
///
/// <para>
/// Sits in <c>Infrastructure/Notifications/</c> alongside
/// <see cref="NotifierTransport"/> because the check is a transport-
/// dependent safety invariant. Exposed as a static public helper so it
/// is unit-testable without building the full DI graph (mirroring how
/// <see cref="NotifierTransport.ResolveImplementation"/> is tested).
/// </para>
/// </summary>
public static class DormancyTransportPrecondition
{
    public const string WarnAfterDaysKey = "Dormancy:Parent:WarnAfterDays";

    /// <summary>
    /// Throws <see cref="System.InvalidOperationException"/> when
    /// <c>Dormancy:Parent:WarnAfterDays &gt; 0</c> and
    /// <paramref name="notifierImpl"/> is not <see cref="SmtpNotifier"/>.
    /// Silently returns otherwise — an unparseable / missing / non-
    /// positive value disables the warn pass entirely, so the
    /// precondition doesn't fire.
    /// </summary>
    public static void Enforce(IConfiguration config, System.Type notifierImpl)
    {
        var raw = config[WarnAfterDaysKey];
        if (!int.TryParse(raw, out var warnAfterDays) || warnAfterDays <= 0)
            return;

        if (notifierImpl == typeof(SmtpNotifier))
            return;

        var resolvedName = notifierImpl == typeof(LoggingNotifier)
            ? NotifierTransport.Log
            : notifierImpl.Name;

        throw new System.InvalidOperationException(
            $"{WarnAfterDaysKey} is enabled ({warnAfterDays} > 0) but " +
            $"Notifications:Transport does not resolve to '{NotifierTransport.Smtp}' " +
            $"(currently: '{resolvedName}'). A dormant-parent warning email " +
            "cannot go to stdout — the worker would mark parents as warned " +
            "without reaching them. Either disable the warn pass by setting " +
            $"{WarnAfterDaysKey} to 0, or configure SMTP via " +
            "Notifications:Transport=smtp and the required SMTP keys.");
    }
}
