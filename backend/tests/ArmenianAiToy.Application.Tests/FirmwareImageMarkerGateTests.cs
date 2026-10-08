using System.Text;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Chip-security review round 3: production ships FirmwareUpdate:BoardModel
/// EMPTY ("offer to every toy"). A field toy on 1.3.2 checks neither the
/// Secure Boot signature nor the in-image marker, so it would install a
/// locked-fleet RELEASE image, refuse its store on an unsecured chip and never
/// come online again. Pins: the staged image's OWN AREGFWV1 marker decides --
/// a secured (-sb) image is offered only to a device reporting exactly that
/// board model, null/legacy board models get nothing, and an image without a
/// marker (or a DEV marker) keeps the old behaviour.
/// </summary>
public class FirmwareImageMarkerGateTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static byte[] Image(params string[] markers)
    {
        var bytes = new List<byte>(Enumerable.Repeat((byte)0xE9, 64));
        foreach (var m in markers)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(m));
            bytes.Add(0);
            bytes.AddRange(Enumerable.Repeat((byte)0xA5, 17));
        }
        return bytes.ToArray();
    }

    private static FirmwareManifestService Service(string imageBoard, string configBoard = "")
        => new(new FirmwareUpdateOptions
        {
            Enabled = true,
            LatestVersion = "1.4.0",
            BoardModel = configBoard,
            ImageBoardModel = imageBoard,
            Url = "https://backend.example/api/devices/firmware-image",
            SizeBytes = 1_708_032,
            Sha256 = "abc123",
            TtlSeconds = 300,
        });

    [Fact]
    public void FindBoardModel_ReadsTheMarker()
    {
        Assert.Equal("areg-s3-n8-sb", FirmwareImageMarker.FindBoardModel(Image("AREGFWV1:1.4.0:areg-s3-n8-sb:release")));
        Assert.Equal("areg-s3-n8", FirmwareImageMarker.FindBoardModel(Image("AREGFWV1:1.4.0:areg-s3-n8:dev")));
    }

    [Fact]
    public void FindBoardModel_NoMarker_IsEmpty()
    {
        Assert.Equal(string.Empty, FirmwareImageMarker.FindBoardModel(Image()));
        // Magic without the terminating NUL is not a marker (same rule as the release gate).
        Assert.Equal(string.Empty, FirmwareImageMarker.FindBoardModel(Encoding.ASCII.GetBytes("AREGFWV1:1.4.0:areg-s3-n8-sb:release")));
    }

    [Fact]
    public void FindBoardModel_DisagreeingMarkers_AreSecuredAndMatchNothing()
    {
        var board = FirmwareImageMarker.FindBoardModel(
            Image("AREGFWV1:1.4.0:areg-s3-n8:dev", "AREGFWV1:1.4.0:areg-s3-n8-sb:release"));
        Assert.Equal(FirmwareImageMarker.InconsistentSecured, board);
        Assert.True(FirmwareImageMarker.IsSecuredBoard(board));
        Assert.False(Service(board).Build("1.3.2", "areg-s3-n8-sb", Now).UpdateAvailable);
    }

    [Fact]
    public void SecuredImage_EmptyConfigBoard_FieldAndLegacyToysGetNothing()
    {
        var svc = Service("areg-s3-n8-sb", configBoard: "");
        Assert.False(svc.Build("1.3.2", null, Now).UpdateAvailable, "legacy toy reports no board model");
        Assert.False(svc.Build("1.3.2", "", Now).UpdateAvailable);
        Assert.False(svc.Build("1.3.2", "areg-s3-n8", Now).UpdateAvailable, "unsecured DEV / field board");
        Assert.False(svc.Build("1.3.2", "AREG-S3-N8-SB", Now).UpdateAvailable, "exact, ordinal match only");
    }

    [Fact]
    public void SecuredImage_IsOfferedToTheSecuredBoard()
    {
        var m = Service("areg-s3-n8-sb", configBoard: "").Build("1.3.9", "areg-s3-n8-sb", Now);
        Assert.True(m.UpdateAvailable);
        Assert.Equal("1.4.0", m.Version);
    }

    [Fact]
    public void SecuredImage_ConfigBoardCannotWidenTheOffer()
    {
        // Even a (wrong) config naming the unsecured board cannot send a locked-fleet image there.
        var svc = Service("areg-s3-n8-sb", configBoard: "areg-s3-n8");
        Assert.False(svc.Build("1.3.2", "areg-s3-n8", Now).UpdateAvailable);
        Assert.False(svc.Build("1.3.2", "areg-s3-n8-sb", Now).UpdateAvailable);
    }

    [Fact]
    public void UnsecuredOrUnmarkedImage_KeepsThePreviousBehaviour()
    {
        Assert.True(Service("areg-s3-n8").Build("1.3.2", null, Now).UpdateAvailable);
        Assert.True(Service(string.Empty).Build("1.3.2", null, Now).UpdateAvailable);
        Assert.True(Service(string.Empty).Build("1.3.2", "areg-s3-n8", Now).UpdateAvailable);
    }

    [Fact]
    public void ReadBoardModel_ReadsTheStagedFile_AndFailsSoftWithoutOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"areg-fw-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, Image("AREGFWV1:1.4.0:areg-s3-n8-sb:release"));
            Assert.Equal("areg-s3-n8-sb", FirmwareImageMarker.ReadBoardModel(path));
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Equal(string.Empty, FirmwareImageMarker.ReadBoardModel(path));
        Assert.Equal(string.Empty, FirmwareImageMarker.ReadBoardModel(""));
        Assert.Equal(string.Empty, FirmwareImageMarker.ReadBoardModel("relative/areg-current.bin"));
    }

    [Fact]
    public void CommittedFieldImage_HasNoSecuredMarker()
    {
        // The image the repo serves today (1.3.x) predates the marker: the gate
        // must leave today's fleet behaviour unchanged.
        var dir = AppContext.BaseDirectory;
        string? repoImage = null;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "src", "ArmenianAiToy.Api", "firmware", "areg-current.bin");
            if (File.Exists(candidate))
            {
                repoImage = candidate;
                break;
            }
        }
        if (repoImage is null)
        {
            return; // image not present in this checkout (moved to private storage) -- nothing to pin
        }
        Assert.False(FirmwareImageMarker.IsSecuredBoard(FirmwareImageMarker.ReadBoardModel(repoImage)));
    }
}
