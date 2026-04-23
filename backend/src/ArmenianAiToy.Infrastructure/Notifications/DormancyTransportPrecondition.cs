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
    public const string AnonymizeAfterDaysKey = "Dormancy:Parent:AnonymizeAfterDays";
    public const string DevicesWarnAfterDaysKey = "Dormancy:Devices:WarnAfterDays";

    /// <summary>
    /// Enforces the dormancy + transport pairing invariants at DI
    /// time. Two guards:
    /// <list type="number">
    ///   <item><description>If <c>Dormancy:Parent:WarnAfterDays &gt; 0</c>
    ///   then the notifier must resolve to <see cref="SmtpNotifier"/>.
    ///   <see cref="LoggingNotifier"/> "delivers" to stdout without
    ///   reaching real parents, and the worker would stamp
    ///   <c>DormancyWarnedAt</c> on every parent on every tick.</description></item>
    ///   <item><description>If
    ///   <c>Dormancy:Parent:AnonymizeAfterDays &gt; 0</c> then
    ///   <c>Dormancy:Parent:WarnAfterDays &gt; 0</c> AND the notifier
    ///   must resolve to <see cref="SmtpNotifier"/>. Destructive
    ///   action without a warn channel is a policy error; destructive
    ///   action on a log transport would silently anonymize parents
    ///   who never received the warning email.</description></item>
    /// </list>
    /// Silently returns when both passes are disabled — the shipped
    /// defaults in <c>appsettings.json</c> are both 0, so a fresh
    /// <c>dotnet run</c> does not trip either guard.
    /// </summary>
    public static void Enforce(IConfiguration config, System.Type notifierImpl)
    {
        var warnRaw = config[WarnAfterDaysKey];
        var warnAfterDays = int.TryParse(warnRaw, out var w) ? w : 0;
        var anonymizeRaw = config[AnonymizeAfterDaysKey];
        var anonymizeAfterDays = int.TryParse(anonymizeRaw, out var a) ? a : 0;

        var resolvedName = notifierImpl == typeof(LoggingNotifier)
            ? NotifierTransport.Log
            : (notifierImpl == typeof(SmtpNotifier) ? NotifierTransport.Smtp : notifierImpl.Name);

        // Guard 1: warn enabled requires SMTP.
        if (warnAfterDays > 0 && notifierImpl != typeof(SmtpNotifier))
        {
            throw new System.InvalidOperationException(
                $"{WarnAfterDaysKey} is enabled ({warnAfterDays} > 0) but " +
                $"Notifications:Transport does not resolve to '{NotifierTransport.Smtp}' " +
                $"(currently: '{resolvedName}'). A dormant-parent warning email " +
                "cannot go to stdout — the worker would mark parents as warned " +
                "without reaching them. Either disable the warn pass by setting " +
                $"{WarnAfterDaysKey} to 0, or configure SMTP via " +
                "Notifications:Transport=smtp and the required SMTP keys.");
        }

        // Guard 2: anonymize enabled requires warn enabled AND SMTP.
        if (anonymizeAfterDays > 0)
        {
            if (warnAfterDays <= 0)
            {
                throw new System.InvalidOperationException(
                    $"{AnonymizeAfterDaysKey} is enabled ({anonymizeAfterDays} > 0) but " +
                    $"{WarnAfterDaysKey} is not ({warnRaw ?? "unset"}). " +
                    "Destructive dormant-parent action requires the warn pass to be " +
                    "enabled — parents must receive a warning email before any " +
                    "irreversible action fires. Either disable destructive action " +
                    $"by setting {AnonymizeAfterDaysKey} to 0, or enable warn by " +
                    $"setting {WarnAfterDaysKey} to a positive value.");
            }
            if (notifierImpl != typeof(SmtpNotifier))
            {
                throw new System.InvalidOperationException(
                    $"{AnonymizeAfterDaysKey} is enabled ({anonymizeAfterDays} > 0) but " +
                    $"Notifications:Transport does not resolve to '{NotifierTransport.Smtp}' " +
                    $"(currently: '{resolvedName}'). A destructive dormancy action " +
                    "cannot run against a log transport — the warning email would " +
                    "never reach the parent, and the action would fire without notice. " +
                    $"Either disable destructive action by setting {AnonymizeAfterDaysKey} " +
                    "to 0, or configure SMTP via Notifications:Transport=smtp and the " +
                    "required SMTP keys.");
            }
        }

        // Guard 3: device-warn enabled requires SMTP. Same posture as
        // Guard 1 — LoggingNotifier always returns "delivered" to the
        // worker without a real send, and the dormant-device worker
        // pass would silently advance Device.DormancyWarnedAt for
        // every dormant device on every tick while no parent received
        // anything. Fail-fast at process start.
        var devicesWarnRaw = config[DevicesWarnAfterDaysKey];
        if (int.TryParse(devicesWarnRaw, out var devicesWarnAfterDays)
            && devicesWarnAfterDays > 0
            && notifierImpl != typeof(SmtpNotifier))
        {
            throw new System.InvalidOperationException(
                $"{DevicesWarnAfterDaysKey} is enabled ({devicesWarnAfterDays} > 0) but " +
                $"Notifications:Transport does not resolve to '{NotifierTransport.Smtp}' " +
                $"(currently: '{resolvedName}'). A dormant-device warning email " +
                "cannot go to stdout — the worker would mark devices as warned " +
                "without reaching their linked parents. Either disable the device-warn " +
                $"pass by setting {DevicesWarnAfterDaysKey} to 0, or configure SMTP " +
                "via Notifications:Transport=smtp and the required SMTP keys.");
        }
    }
}
