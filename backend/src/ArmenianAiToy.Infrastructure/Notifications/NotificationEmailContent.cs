namespace ArmenianAiToy.Infrastructure.Notifications;

/// <summary>
/// Shared email content for real-delivery <see cref="ArmenianAiToy.Application.Notifications.INotifier"/>
/// implementations (<see cref="SmtpNotifier"/>, <see cref="ResendNotifier"/>).
/// Subjects, link builders, and plain-text Armenian bodies live here so the
/// two transports can never drift apart on parent-facing copy or on the
/// link format the dashboard's boot router depends on
/// (<c>?token=</c> for reset, <c>?verifyToken=</c> for verification —
/// consumed by <c>parent.html</c>'s <c>bootRouter()</c>).
///
/// <para>
/// Extracted verbatim from <c>SmtpNotifier</c>'s private statics when the
/// Resend transport landed — output is byte-identical to the pre-extraction
/// bodies, pinned by the existing <c>SmtpNotifierTests</c>.
/// <see cref="LoggingNotifier"/> deliberately does not use this class: it
/// never composes a message.
/// </para>
/// </summary>
internal static class NotificationEmailContent
{
    internal const string PasswordResetSubject = "Գաղտնաբառի վերականգնում";
    internal const string EmailVerificationSubject = "Հաստատեք Ձեր էլ. փոստը";
    internal const string DormancyWarningSubject = "Ձեր հաշիվը երկար ժամանակ անգործուն է";
    internal const string DormantDeviceWarningSubject = "Ձեր երեխայի խաղալիքը վերջերս չի օգտագործվել";
    internal const string ToyJoinedSubject = "Ձեր խաղալիքին միացել է ևս մեկ ծնող";

    internal static string BuildResetLink(string linkBase, string rawToken)
    {
        // NotifierTransport.ResolveImplementation already validated that
        // linkBase is non-empty when a real-delivery transport is selected;
        // we still guard here defensively so a reconfigured process never
        // constructs a bare-token "link". Token is URL-encoded even though
        // the current token charset is URL-safe, because that's the
        // behaviour a future caller would expect.
        var prefix = string.IsNullOrWhiteSpace(linkBase) ? "" : linkBase.Trim();
        var separator = prefix.Contains('?') ? '&' : '?';
        return $"{prefix}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    internal static string BuildVerificationLink(string linkBase, string rawToken)
    {
        // Parallel to BuildResetLink — uses `verifyToken` instead of
        // `token` so the dashboard boot router can distinguish the
        // two flows by query-param name. URL-encoded even though the
        // current token charset is URL-safe, for caller-expected
        // behavior.
        var prefix = string.IsNullOrWhiteSpace(linkBase) ? "" : linkBase.Trim();
        var separator = prefix.Contains('?') ? '&' : '?';
        return $"{prefix}{separator}verifyToken={Uri.EscapeDataString(rawToken)}";
    }

    internal static string BuildPasswordResetBody(string resetLink)
    {
        // Eastern Armenian, natural tone — matches the product's
        // Armenian-first posture. Keeps the copy short and honest:
        // what this is, how long it is valid, and what to do if the
        // recipient did not request it. No marketing surface, no
        // unsubscribe footer (this is a transactional / account-
        // recovery mail, not bulk).
        return string.Join("\n\n",
            "Գաղտնաբառի վերականգնման հղումը ստորև է։",
            "Այս հղումը վավեր է սահմանափակ ժամանակ։",
            "Եթե Դուք այս խնդրանքը չեք արել, անտեսեք այս նամակը։",
            resetLink);
    }

    internal static string BuildEmailVerificationBody(string verificationLink)
    {
        // Eastern Armenian, warm and neutral. Lead: what happened
        // (someone registered with this email). Middle: what to do
        // (click the link). Footer: how long the link lives, and
        // what to do if they didn't register (ignore). No urgency
        // copy — verification isn't time-critical.
        return string.Join("\n\n",
            "Բարև, Ձեր էլ. փոստի հասցեն օգտագործվել է Areg-ի հաշիվ ստեղծելու համար։",
            "Խնդրում ենք հաստատել, որ այս հասցեն Ձերն է՝ սեղմելով ստորև դրված հղման վրա։",
            "Հղումը վավեր է 7 օրվա ընթացքում։",
            "Եթե Դուք հաշիվ չեք ստեղծել, այս նամակը կարող եք անտեսել։",
            verificationLink);
    }

    internal static string BuildDormancyWarningBody(DateTime? deleteAtUtc)
    {
        // Eastern Armenian, warm but neutral in the warn-only case;
        // factual-without-panic when a destructive date is attached.
        // The ordering is: what happened → what to do → (optional)
        // when it will be acted upon → opt-out note. The extra
        // middle sentence only appears when the destructive pass is
        // enabled (deleteAtUtc non-null).
        var lines = new System.Collections.Generic.List<string>
        {
            "Ձեր հաշիվը վերջերս չի օգտագործվել։",
            "Եթե դեռ ցանկանում եք պահպանել Ձեր հաշիվը և տվյալները, խնդրում ենք կրկին մուտք գործել։"
        };
        if (deleteAtUtc.HasValue)
        {
            // Plain yyyy-MM-dd — no locale-specific formatting, no
            // time component. The parent sees a bounded calendar
            // date they can verify against their calendar.
            var dateStr = deleteAtUtc.Value.ToString("yyyy-MM-dd");
            lines.Add(
                $"Եթե մինչև {dateStr} չմտնեք, Ձեր անձնական տվյալները ինքնաբար կհեռացվեն։");
        }
        lines.Add("Եթե Դուք այլևս չեք օգտագործում այս հաշիվը, այս նամակը կարող եք անտեսել։");
        return string.Join("\n\n", lines);
    }

    internal static string BuildDormantDeviceWarningBody(
        string deviceName, DateTime lastSeenAtUtc, DateTime? deleteAtUtc)
    {
        // Eastern Armenian, warm but neutral in the warn-only case;
        // factual-without-panic when a destructive date is attached.
        // Mirrors BuildDormancyWarningBody's shape. When deleteAtUtc
        // is non-null the body swaps in an explicit delete-date line
        // so the parent is forewarned before the destructive pass
        // fires on a later tick. No time component, no locale-
        // specific format — plain yyyy-MM-dd so a regex-assert test
        // can pin the shape.
        var lastSeenStr = lastSeenAtUtc.ToString("yyyy-MM-dd");
        var lines = new System.Collections.Generic.List<string>
        {
            $"Բարև։ Ձեր Areg հաշվին կապված սարք-ը — {deviceName} — վերջերս չի օգտագործվել։",
            $"Վերջին ակտիվությունը: {lastSeenStr}.",
            "Եթե դեռ ցանկանում եք պահպանել այս սարքը ակտիվ, պարզապես կրկին օգտագործեք այն։"
        };
        if (deleteAtUtc.HasValue)
        {
            var deleteStr = deleteAtUtc.Value.ToString("yyyy-MM-dd");
            lines.Add(
                $"Եթե մինչև {deleteStr} սարքը չօգտագործվի, այն և իր տվյալները ինքնաբար կհեռացվեն։");
        }
        lines.Add("Եթե այլևս չեք օգտագործում այս սարքը, այս նամակը կարող եք անտեսել։");
        return string.Join("\n\n", lines);
    }

    internal static string BuildToyJoinedBody(string deviceName)
    {
        // Says WHAT happened and WHAT TO DO — never WHO joined. The other
        // parent's address is not ours to hand out, and the recipient does
        // not need it to act: the action is on the toy, not the person.
        // Calm rather than alarming: in the ordinary case this is the second
        // parent in the household doing exactly what they should.
        return string.Join("\n\n", new[]
        {
            $"Բարև։ «{deviceName}» խաղալիքին միացել է ևս մեկ ծնող։",
            // The reassurance comes SECOND, before the "what to do" line.
            // Without it the mail reads as an alarm, and in the ordinary
            // case this is simply the other parent in the household.
            "Ամենայն հավանականությամբ դա Ձեր ընտանիքի մյուս ծնողն է։",
            "Այժմ նա նույնպես կարող է տեսնել այս խաղալիքի զրույցները և փոխել կարգավորումները։",
            "Եթե դա Ձեր ընտանիքից չէ, բացեք ծնողական վահանակը, կասեցրեք խաղալիքի մուտքը և կապվեք մեզ հետ։"
        });
    }
}
