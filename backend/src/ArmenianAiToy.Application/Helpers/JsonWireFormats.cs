namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// The exact string form every <see cref="System.DateTime"/> takes on this
/// API's wire.
///
/// <para>This exists because two places have to agree on it byte-for-byte and
/// they live in different projects. <c>UtcDateTimeConverter</c> (Api) writes
/// every timestamp; <c>FirmwareManifestService.Sign</c> (Application) has to
/// HMAC the same text, because the device can only rebuild the canonical
/// string from the JSON it actually received.</para>
///
/// <para><b>They disagreed, and it broke real OTA updates.</b> The converter
/// writes seven fractional digits ALWAYS; <c>Sign</c> used
/// <c>JsonSerializer.Serialize(dateTime)</c>, whose default writer TRIMS
/// trailing zeros. So whenever a manifest's <c>expiresAt</c> happened to land
/// on a tick ending in zero, the signed text and the sent text differed by
/// those characters, the toy computed a different HMAC, and it refused the
/// update with <c>manifest_sig_invalid</c> — having downloaded nothing and
/// with no way to tell that the SERVER was at fault. Roughly one attempt in
/// ten, silently, since manifest signing shipped. Observed on 2026-08-14
/// refusing firmware 1.3.0 after 1.2.1 and 1.2.2 had both applied cleanly.</para>
///
/// <para>The unit test that was supposed to pin this serialized the response
/// with a bare <c>JsonSerializerOptions</c> and never registered the
/// converter, so it reproduced <c>Sign</c>'s own mistake and passed. A test
/// that builds its own idea of the wire is not testing the wire.</para>
/// </summary>
public static class JsonWireFormats
{
    /// <summary>
    /// ISO-8601 UTC with a literal <c>Z</c> and a FIXED seven fractional
    /// digits — trailing zeros included, because a trimmed one is a different
    /// string and this text gets signed.
    /// </summary>
    public const string UtcDateTime = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
}
