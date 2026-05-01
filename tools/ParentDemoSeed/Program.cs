// ParentDemoSeed — local-only console tool that seeds the parent
// dashboard with reproducible demo data so a manual browser QA pass
// can drive every meaningful surface (Today panel, ▶ Listen, "Your
// activity" feed) without recorded toy sessions, ESP hardware, or
// any OpenAI key.
//
// HARD CONSTRAINTS (mirrored in CLAUDE.md operating rules):
//  - Direct EF only. NEVER goes through public HTTP endpoints.
//  - NO calls to ChatService, IModerationService, IAudioSynthesisService,
//    IAudioTranscriptionService, INotifier, IGoogleIdTokenValidator, or
//    any OpenAI SDK type.
//  - System-actor audit rows (ConversationsPurgedByRetention,
//    ParentDormancyWarned, ParentDormancyAnonymized, DeviceDormancyWarned,
//    DeviceDormancyDeleted) are NOT seeded — they're invisible to the
//    parent feed by design (see CLAUDE.md § Audit events) and seeding
//    them would muddy the contract.
//  - Idempotent. Wipes ONLY rows matching the demo Email + MAC pair
//    and then re-creates the graph from scratch.
//  - Safety-gated. Refuses to run unless ASPNETCORE_ENVIRONMENT is
//    Development, --i-understand-local-demo is passed, or
//    AAT_DEMO_SEED_OK=1 is set.
//  - Never mutates a non-demo row. If a Parent or Device exists but
//    its identifiers don't match the demo pair exactly, the seeder
//    aborts loudly.
//
// Usage:
//   dotnet run --project tools/ParentDemoSeed
//   dotnet run --project tools/ParentDemoSeed -- --reset
//   dotnet run --project tools/ParentDemoSeed -- --db <path>
//   dotnet run --project tools/ParentDemoSeed -- --i-understand-local-demo

using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Tools.ParentDemoSeed;

internal static class Program
{
    // ---------- Demo identifiers ----------
    private const string DemoEmail = "parent-demo@example.local";
    private const string DemoPassword = "DemoPassw0rd!";
    private const string DemoMac = "DE:MO:DE:V0:01";
    private const string DemoDeviceName = "Areg Demo Device";
    private const string DemoApiKey = "dtk_demo_local_only_do_not_distribute";
    private const string DemoChildName = "Արեգ";
    private const string DemoTimeZoneId = "Asia/Yerevan";
    private const string TermsVersion = "1.0";

    // Default DB path (relative to repo root). Matches the location used
    // when running the API via `dotnet run --project backend/src/
    // ArmenianAiToy.Api`, since the working directory then is the API
    // project directory.
    private const string DefaultDbPath =
        "backend/src/ArmenianAiToy.Api/armenian_ai_toy.db";

    // Synthetic local-demo audio fixture: a single MPEG-1 Layer III frame
    // (header + zero-filled body, 104 bytes total). NOT a TTS render.
    // Decoded into:
    //   audio-blobs/{conv:N}/{assistantMessageId:N}.mp3
    // for the Riddle conversation's assistant turn so the C2.1 ▶ Listen
    // affordance and the Today panel's "🔊 N replayable" badge have
    // something to point at. Browsers may render this as silent or
    // refuse to decode the zero-filled body — either way the dashboard
    // surfaces (badge count, button visibility, network 200 with
    // `audio/mpeg`) are exercised honestly.
    //
    // Frame header bytes: 0xFF 0xFB 0x10 0xC4
    //   - sync 0xFF / V1 L3 no-CRC 0xFB
    //   - bitrate idx 0001 (32kbps), sample 00 (44.1kHz), pad 0
    //   - channel 11 (mono), original 1, emphasis 00
    //   Frame size = 144 * 32000 / 44100 = 104 bytes (no padding).
    private const string SilentMp3Base64 =
        "//sQxAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAA=";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var reset = args.Contains("--reset");
        var localDemoOverride = args.Contains("--i-understand-local-demo");
        var dbPath = ParseStringArg(args, "--db") ?? DefaultDbPath;

        if (!IsAllowedToRun(localDemoOverride, out var refusal))
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }

        if (LooksLikeProductionPath(dbPath, out var prodReason))
        {
            Console.Error.WriteLine(
                $"ParentDemoSeed: refusing --db path that looks " +
                $"production-shaped: {dbPath} ({prodReason})");
            return 2;
        }

        var fullDbPath = Path.GetFullPath(dbPath);
        if (!File.Exists(fullDbPath))
        {
            Console.Error.WriteLine(
                $"ParentDemoSeed: DB file not found: {fullDbPath}\n" +
                "Run the API once (`dotnet run --project " +
                "backend/src/ArmenianAiToy.Api`) to apply migrations, " +
                "or pass --db <path> to an existing dev DB.");
            return 1;
        }

        var audioRoot = Path.Combine(
            Path.GetDirectoryName(fullDbPath)!, "audio-blobs");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + fullDbPath)
            .Options;
        await using var db = new AppDbContext(options);

        try
        {
            await WipeDemoDataAsync(db, audioRoot);
        }
        catch (DemoSeedAbortException ex)
        {
            Console.Error.WriteLine("ParentDemoSeed: " + ex.Message);
            return 3;
        }

        if (reset)
        {
            Console.WriteLine(
                $"ParentDemoSeed: demo data wiped from {fullDbPath}");
            return 0;
        }

        var summary = await SeedDemoDataAsync(db, audioRoot);

        PrintSeededSummary(fullDbPath, audioRoot, summary);
        return 0;
    }

    // ---------- safety gates ----------

    private static bool IsAllowedToRun(bool localDemoOverride, out string refusal)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var inDevelopment = string.Equals(
            env, "Development", StringComparison.OrdinalIgnoreCase);
        var envOk = string.Equals(
            Environment.GetEnvironmentVariable("AAT_DEMO_SEED_OK"), "1",
            StringComparison.Ordinal);

        if (inDevelopment || localDemoOverride || envOk)
        {
            refusal = string.Empty;
            return true;
        }

        refusal =
            "ParentDemoSeed refuses to run without an explicit local-demo " +
            "signal.\n\nPick one:\n" +
            "  - export ASPNETCORE_ENVIRONMENT=Development\n" +
            "  - pass --i-understand-local-demo on the command line\n" +
            "  - export AAT_DEMO_SEED_OK=1\n";
        return false;
    }

    private static bool LooksLikeProductionPath(string path, out string reason)
    {
        // Cheap structural heuristics. The intent is "make a foot-gun
        // unlikely on a shared host," not "harden against a determined
        // operator." A real production DB on a dev box should never carry
        // these substrings.
        var normalised = path.Replace('\\', '/').ToLowerInvariant();
        if (normalised.StartsWith("/srv/", StringComparison.Ordinal))
        {
            reason = "starts with /srv/";
            return true;
        }
        if (normalised.StartsWith("/var/lib/", StringComparison.Ordinal))
        {
            reason = "starts with /var/lib/";
            return true;
        }
        if (normalised.Contains("prod", StringComparison.Ordinal))
        {
            reason = "contains 'prod'";
            return true;
        }
        if (normalised.Contains("staging", StringComparison.Ordinal))
        {
            reason = "contains 'staging'";
            return true;
        }
        if (normalised.Contains("live", StringComparison.Ordinal))
        {
            reason = "contains 'live'";
            return true;
        }
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            reason = "looks like a UNC network path";
            return true;
        }
        reason = string.Empty;
        return false;
    }

    private static string? ParseStringArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ParentDemoSeed — local parent-dashboard QA seed.\n");
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  dotnet run --project tools/ParentDemoSeed [-- <args>]");
        Console.WriteLine();
        Console.WriteLine("Args:");
        Console.WriteLine("  --reset                   wipe demo data only");
        Console.WriteLine(
            "  --db <path>               override SQLite DB path");
        Console.WriteLine(
            "  --i-understand-local-demo allow run outside Development");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine(
            "  ASPNETCORE_ENVIRONMENT=Development     allows run");
        Console.WriteLine(
            "  AAT_DEMO_SEED_OK=1                     allows run");
    }

    // ---------- wipe ----------

    private static async Task WipeDemoDataAsync(
        AppDbContext db, string audioRoot)
    {
        var parent = await db.Parents
            .FirstOrDefaultAsync(p => p.Email == DemoEmail);
        var device = await db.Devices
            .FirstOrDefaultAsync(d => d.MacAddress == DemoMac);

        // Identity guard. We queried by demo predicates, so a row that
        // came back must already match — but verify explicitly so any
        // future caller bug that swapped the predicates is loud, not
        // silent.
        if (parent is not null && parent.Email != DemoEmail)
        {
            throw new DemoSeedAbortException(
                "Parent row found without demo email; refusing to wipe.");
        }
        if (device is not null && device.MacAddress != DemoMac)
        {
            throw new DemoSeedAbortException(
                "Device row found without demo MAC; refusing to wipe.");
        }

        if (device is not null)
        {
            // Clean the audio blob directories BEFORE conversation rows
            // disappear so we still know the conversation ids.
            var convIds = await db.Conversations
                .Where(c => c.DeviceId == device.Id)
                .Select(c => c.Id)
                .ToListAsync();

            foreach (var cid in convIds)
            {
                var dir = Path.Combine(audioRoot, cid.ToString("N"));
                if (Directory.Exists(dir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"  warn: failed to remove {dir}: {ex.Message}");
                    }
                }
            }

            await db.Messages
                .Where(m => convIds.Contains(m.ConversationId))
                .ExecuteDeleteAsync();
            await db.Conversations
                .Where(c => c.DeviceId == device.Id)
                .ExecuteDeleteAsync();
            await db.Children
                .Where(c => c.DeviceId == device.Id)
                .ExecuteDeleteAsync();
        }

        if (parent is not null)
        {
            await db.AuditEvents
                .Where(a => a.ActorParentId == parent.Id)
                .ExecuteDeleteAsync();
        }

        if (parent is not null && device is not null)
        {
            await db.ParentDevices
                .Where(pd => pd.ParentId == parent.Id
                          && pd.DeviceId == device.Id)
                .ExecuteDeleteAsync();
        }

        if (device is not null)
        {
            await db.Devices
                .Where(d => d.Id == device.Id)
                .ExecuteDeleteAsync();
        }

        if (parent is not null)
        {
            // FK cascades take Parent*Token rows; delete the Parent last.
            await db.Parents
                .Where(p => p.Id == parent.Id)
                .ExecuteDeleteAsync();
        }
    }

    // ---------- seed ----------

    private record SeedSummary(
        int Conversations,
        int Messages,
        int FlaggedMessages,
        int ReplayableAssistantMessages);

    private static async Task<SeedSummary> SeedDemoDataAsync(
        AppDbContext db, string audioRoot)
    {
        var nowUtc = DateTime.UtcNow;
        var yerevan = TimeZoneInfo.FindSystemTimeZoneById(DemoTimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, yerevan);
        var todayLocal = nowLocal.Date;

        DateTime LocalTodayUtc(int hour, int minute)
        {
            var local = new DateTime(
                todayLocal.Year, todayLocal.Month, todayLocal.Day,
                hour, minute, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, yerevan);
        }

        // ---- Parent / Device / link / Child ----
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var parent = new Parent
        {
            Id = parentId,
            Email = DemoEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoPassword),
            RegisteredAt = nowUtc.AddDays(-30),
            TermsAcceptedAt = nowUtc.AddDays(-30),
            TermsVersion = TermsVersion,
            EmailVerifiedAt = nowUtc.AddDays(-29),
            LastLoginAt = nowUtc.AddHours(-1),
        };

        var device = new Device
        {
            Id = deviceId,
            MacAddress = DemoMac,
            Name = DemoDeviceName,
            ApiKey = DemoApiKey,
            RegisteredAt = nowUtc.AddDays(-30),
            LastSeenAt = nowUtc.AddMinutes(-5),
            TimeZone = DemoTimeZoneId,
            IsPaused = false,
            BedtimeStart = null,
            BedtimeEnd = null,
            StoryEnabled = true,
            GameEnabled = true,
            RiddleEnabled = true,
            CuriosityEnabled = true,
        };

        var link = new ParentDevice
        {
            ParentId = parentId,
            DeviceId = deviceId,
            LinkedAt = nowUtc.AddDays(-30),
        };

        // 5-year-old per the spec; June 12 is arbitrary but stable.
        var child = new Child
        {
            Id = childId,
            DeviceId = deviceId,
            Name = DemoChildName,
            Gender = Gender.Boy,
            DateOfBirth = new DateOnly(nowUtc.Year - 5, 6, 12),
            StoryEnabled = null,
            GameEnabled = null,
            RiddleEnabled = null,
            CuriosityEnabled = null,
        };

        db.Parents.Add(parent);
        db.Devices.Add(device);
        db.ParentDevices.Add(link);
        db.Children.Add(child);

        // ---- Conversations + messages ----
        // Schedule conversations across the Yerevan-local day. Some times
        // may be in the future relative to nowLocal — the Today panel
        // filter is `Timestamp >= dayStartUtc` with no upper bound, so
        // they still surface; the demo just may include "later today"
        // entries. Re-running the seeder regenerates fresh timestamps.

        var c1Start = LocalTodayUtc(9, 0);
        var c1 = NewConversation(deviceId, childId, c1Start);
        var c1Msgs = new[]
        {
            NewMessage(c1.Id, MessageRole.User,
                "Պատմիր հեքիաթ", c1Start),
            NewMessage(c1.Id, MessageRole.Assistant,
                "Մի անգամ մի անտառում ապրում էր մի թռչնակ։ " +
                "Նրա անունը Չորիկ էր, ու նա շատ էր սիրում երգել։",
                c1Start.AddSeconds(2)),
            NewMessage(c1.Id, MessageRole.User,
                "Ինչ եղավ հետո՞", c1Start.AddSeconds(30)),
            NewMessage(c1.Id, MessageRole.Assistant,
                "Չորիկը գտավ մի փոքրիկ ընկեր՝ սպիտակ նապաստակ։ " +
                "Միասին նրանք քայլեցին դեպի կախարդական լիճը։",
                c1Start.AddSeconds(32)),
        };

        var c2Start = LocalTodayUtc(11, 30);
        var c2 = NewConversation(deviceId, childId, c2Start);
        var c2Msgs = new[]
        {
            NewMessage(c2.Id, MessageRole.User,
                "Խաղենք", c2Start),
            NewMessage(c2.Id, MessageRole.Assistant,
                "Հա, մի թաքստոց խաղանք։ Ես պահվում եմ, դու փնտրիր։",
                c2Start.AddSeconds(2)),
            NewMessage(c2.Id, MessageRole.User,
                "Հա", c2Start.AddSeconds(10)),
            NewMessage(c2.Id, MessageRole.Assistant,
                "Փակի՛ր աչքերդ, ու հաշվիր մինչև տասը։ " +
                "Մեկ, երկու, երեք...",
                c2Start.AddSeconds(12)),
        };

        var c3Start = LocalTodayUtc(14, 0);
        var c3 = NewConversation(deviceId, childId, c3Start);
        var c3RiddleMessageId = Guid.NewGuid();
        var audioRelativePath =
            $"{c3.Id:N}/{c3RiddleMessageId:N}.mp3";
        var c3Msgs = new[]
        {
            NewMessage(c3.Id, MessageRole.User,
                "Հանելուկ տուր", c3Start),
            new Message
            {
                Id = c3RiddleMessageId,
                ConversationId = c3.Id,
                Role = MessageRole.Assistant,
                Content =
                    "Ինչը գիշերն է փայլում, բայց ձեռքով չի բռնվում?",
                Timestamp = c3Start.AddSeconds(2),
                SafetyFlag = SafetyFlag.Clean,
                AudioBlobPath = audioRelativePath,
            },
            NewMessage(c3.Id, MessageRole.User,
                "Աստղ", c3Start.AddSeconds(20)),
            NewMessage(c3.Id, MessageRole.Assistant,
                "Ճիշտ ես!", c3Start.AddSeconds(22)),
        };

        var c4Start = LocalTodayUtc(16, 30);
        var c4 = NewConversation(deviceId, childId, c4Start);
        var c4Msgs = new[]
        {
            NewMessage(c4.Id, MessageRole.User,
                "Ինչու երկինքը կապույտ է?", c4Start),
            NewMessage(c4.Id, MessageRole.Assistant,
                "Արևի լույսը երկնքից անցնելիս կապույտ գույնն է " +
                "ամենից շատ ցրվում։ Դրա համար երկինքը մեզ կապույտ է երևում։",
                c4Start.AddSeconds(2)),
        };

        var c5Start = LocalTodayUtc(18, 0);
        var c5 = NewConversation(deviceId, childId, c5Start);
        var c5Msgs = new[]
        {
            new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = c5.Id,
                Role = MessageRole.User,
                Content = "ուտեմ կրակ",
                Timestamp = c5Start,
                SafetyFlag = SafetyFlag.Flagged,
                AudioBlobPath = null,
            },
            new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = c5.Id,
                Role = MessageRole.Assistant,
                Content = "Արի, մի հեքիաթ սկսենք։",
                Timestamp = c5Start.AddSeconds(1),
                SafetyFlag = SafetyFlag.Blocked,
                AudioBlobPath = null,
            },
        };

        db.Conversations.AddRange(c1, c2, c3, c4, c5);
        db.Messages.AddRange(c1Msgs);
        db.Messages.AddRange(c2Msgs);
        db.Messages.AddRange(c3Msgs);
        db.Messages.AddRange(c4Msgs);
        db.Messages.AddRange(c5Msgs);

        // ---- Audit events (parent-actor only). ----
        // Use AuditEvent factories where available; override the
        // factory-set Timestamp so the feed has visible spread.
        // System-actor events (ActorParentId == null) are out of scope —
        // they would not appear in the parent feed by design.
        var auditEvents = new List<AuditEvent>();
        AuditEvent Stamp(AuditEvent ev, DateTime ts)
        {
            ev.Timestamp = ts;
            return ev;
        }

        auditEvents.Add(Stamp(
            AuditEvent.ParentDevicePauseStateChanged(parentId, deviceId, true),
            nowUtc.AddHours(-6)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentDevicePauseStateChanged(parentId, deviceId, false),
            nowUtc.AddHours(-6).AddMinutes(2)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentBedtimeWindowSet(parentId, deviceId,
                new TimeOnly(22, 0), new TimeOnly(7, 0)),
            nowUtc.AddHours(-5)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentDeviceModeFlagsSet(
                parentId, deviceId, true, true, true, true),
            nowUtc.AddHours(-4)));
        auditEvents.Add(Stamp(
            AuditEvent.ChildModeOverridesSet(
                parentId, childId, null, null, null, null),
            nowUtc.AddHours(-3)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentDataExported(
                parentId,
                devices: 1, children: 1,
                conversations: 5, messages: 16, auditEvents: 6),
            nowUtc.AddHours(-2)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentEmailVerified(parentId),
            nowUtc.AddHours(-1)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentPasswordResetRequested(parentId),
            nowUtc.AddMinutes(-30)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentPasswordResetCompleted(parentId),
            nowUtc.AddMinutes(-15)));
        auditEvents.Add(Stamp(
            AuditEvent.ParentGoogleSignIn(parentId,
                firstTime: false, linkedToPasswordAccount: false),
            nowUtc.AddMinutes(-5)));

        db.AuditEvents.AddRange(auditEvents);

        await db.SaveChangesAsync();

        // ---- Audio blob fixture. ----
        // Write the silent MP3 AFTER SaveChanges to ensure that if EF
        // throws, we don't leave orphan bytes on disk for a row that
        // never persisted.
        Directory.CreateDirectory(
            Path.Combine(audioRoot, c3.Id.ToString("N")));
        var audioFullPath = Path.Combine(audioRoot, audioRelativePath);
        await File.WriteAllBytesAsync(
            audioFullPath, Convert.FromBase64String(SilentMp3Base64));

        var allMessages = c1Msgs.Concat(c2Msgs).Concat(c3Msgs)
            .Concat(c4Msgs).Concat(c5Msgs).ToList();

        var flagged = allMessages.Count(m => m.SafetyFlag != SafetyFlag.Clean);
        var replayable = allMessages.Count(m =>
            m.Role == MessageRole.Assistant && m.AudioBlobPath != null);

        return new SeedSummary(
            Conversations: 5,
            Messages: allMessages.Count,
            FlaggedMessages: flagged,
            ReplayableAssistantMessages: replayable);
    }

    private static Conversation NewConversation(
        Guid deviceId, Guid childId, DateTime startedAtUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            ChildId = childId,
            StartedAt = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc),
            EndedAt = null,
        };

    private static Message NewMessage(
        Guid conversationId, MessageRole role, string content, DateTime tsUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Timestamp = DateTime.SpecifyKind(tsUtc, DateTimeKind.Utc),
            SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = null,
        };

    // ---------- output ----------

    private static void PrintSeededSummary(
        string dbPath, string audioRoot, SeedSummary s)
    {
        var bar = new string('=', 60);
        Console.WriteLine();
        Console.WriteLine(bar);
        Console.WriteLine("  Areg Parent Dashboard — Demo Data Seeded");
        Console.WriteLine(bar);
        Console.WriteLine($"  URL:        http://localhost:5000/parent.html");
        Console.WriteLine($"  Email:      {DemoEmail}");
        Console.WriteLine($"  Password:   {DemoPassword}");
        Console.WriteLine($"  Device:     {DemoDeviceName}");
        Console.WriteLine($"  Timezone:   {DemoTimeZoneId}");
        Console.WriteLine($"  Child:      {DemoChildName} (5)");
        Console.WriteLine();
        Console.WriteLine($"  Today ({DemoTimeZoneId}):");
        Console.WriteLine($"    {s.Conversations} conversations");
        Console.WriteLine($"    {s.Messages} messages");
        Console.WriteLine($"    {s.FlaggedMessages} flagged (Flagged + Blocked)");
        Console.WriteLine($"    {s.ReplayableAssistantMessages} replayable assistant audio");
        Console.WriteLine();
        Console.WriteLine($"  DB:         {dbPath}");
        Console.WriteLine($"  Audio root: {audioRoot}");
        Console.WriteLine(bar);
    }

    private sealed class DemoSeedAbortException : Exception
    {
        public DemoSeedAbortException(string message) : base(message) { }
    }
}
