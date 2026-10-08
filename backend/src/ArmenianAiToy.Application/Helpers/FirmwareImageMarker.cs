using System.Text;
using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Reads the signed in-image version marker every firmware since the chip-
/// security work carries: <c>AREGFWV1:&lt;version&gt;:&lt;board&gt;:&lt;profile&gt;\0</c>
/// (esp32/AregVoiceMvp/fw_version_marker.cpp; the same pattern
/// tools/firmware/check_release_image.py checks). Pure; no I/O except
/// <see cref="ReadBoardModel"/>.
/// <para>
/// Why the backend reads it (review round 3): production ships
/// <c>FirmwareUpdate:BoardModel</c> EMPTY, which means "offer to every toy". A
/// field toy on 1.3.2 checks neither signature nor marker, so it would install
/// the first Secure-Boot-signed RELEASE image staged, refuse its store on an
/// unsecured chip and never come online again -- no OTA way back. The image's
/// OWN marker therefore decides: an image built for a secured board (board
/// model ending in <c>-sb</c>) is offered only to a device reporting exactly
/// that board model, whatever the config says.
/// </para>
/// </summary>
public static class FirmwareImageMarker
{
    /// <summary>Board-model suffix of the secured (locked) fleet.</summary>
    public const string SecuredSuffix = "-sb";

    /// <summary>Returned when an image carries markers naming DIFFERENT boards:
    /// secured (so the guard applies) and matching no real device.</summary>
    public const string InconsistentSecured = "inconsistent-marker-sb";

    // Same byte classes as check_release_image.py's MARKER_RE.
    private static readonly Regex MarkerRe = new(
        @"AREGFWV1:([0-9.]+):([\x21-\x39\x3b-\x7e]+):([a-z]+)\x00",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True for a board model of the secured fleet (<c>...-sb</c>).</summary>
    public static bool IsSecuredBoard(string? boardModel)
        => !string.IsNullOrEmpty(boardModel)
           && boardModel.EndsWith(SecuredSuffix, StringComparison.Ordinal);

    /// <summary>The board model the image's marker names; empty when the image
    /// has no marker (pre-1.4 firmware); <see cref="InconsistentSecured"/> when
    /// markers disagree.</summary>
    public static string FindBoardModel(byte[] image)
    {
        // Latin1 maps every byte to one char, so offsets and bytes line up.
        var text = Encoding.Latin1.GetString(image);
        var boards = MarkerRe.Matches(text).Select(m => m.Groups[2].Value).Distinct(StringComparer.Ordinal).ToList();
        return boards.Count switch
        {
            0 => string.Empty,
            1 => boards[0],
            _ => InconsistentSecured,
        };
    }

    /// <summary>Reads the configured image once (startup). Missing / relative /
    /// unreadable path -> empty (nothing to inspect; the image endpoint 404s
    /// for the same path anyway).</summary>
    public static string ReadBoardModel(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !Path.IsPathRooted(imagePath) || !File.Exists(imagePath))
        {
            return string.Empty;
        }
        try
        {
            return FindBoardModel(File.ReadAllBytes(imagePath));
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
