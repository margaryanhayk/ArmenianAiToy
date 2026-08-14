using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArmenianAiToy.Api.Serialization;

/// <summary>
/// Serializes every <see cref="DateTime"/> on the wire as UTC ISO-8601
/// with an explicit <c>Z</c> designator.
///
/// <para>The bug this fixes: persisted timestamps round-trip through
/// SQLite as <see cref="DateTimeKind.Unspecified"/>, and the default
/// System.Text.Json writer emits no offset for Unspecified/Utc kinds.
/// A browser parsing an offset-less date-time string treats it as
/// <em>local</em> time (ECMAScript), so the parent dashboard rendered
/// every conversation/message/audit time shifted by the viewer's UTC
/// offset (e.g. −4h for Asia/Yerevan). Meanwhile in-memory values that
/// were <c>Kind=Utc</c> (e.g. <c>generatedAt</c>) did carry <c>Z</c> —
/// the inconsistency inside a single payload was the tell.</para>
///
/// <para>All persisted times in this system are UTC by construction
/// (<c>DateTime.UtcNow</c> at every write site), so stamping Unspecified
/// as UTC on the way out is correct, not a guess. Utc stays Utc; a Local
/// value (none are persisted) is converted to UTC.</para>
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc.ToString(ArmenianAiToy.Application.Helpers.JsonWireFormats.UtcDateTime,
            System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>Nullable companion so <c>DateTime?</c> members (EndedAt,
/// LastSeenAt, EmailVerifiedAt, …) get the same <c>Z</c> treatment.</summary>
public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly UtcDateTimeConverter Inner = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return Inner.Read(ref reader, typeof(DateTime), options);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        Inner.Write(writer, value.Value, options);
    }
}
