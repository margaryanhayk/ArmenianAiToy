using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Variant endings — the additive <c>altOf</c> field that marks a manifest
/// entry as an ALTERNATE ENDING of another story rather than a story of its
/// own.
/// <para>
/// Structured as a deliberate copy of <see cref="ContentSyncSeriesTests"/>,
/// the previous additive field group on this contract — with ONE rule
/// intentionally inverted, which is most of what these tests exist to pin: a
/// bad series pairing drops the FIELD and keeps the story, a bad
/// <c>altOf</c> drops the ITEM. See <c>ContentManifestService.TryResolveAltOf</c>.
/// </para>
/// </summary>
public class ContentSyncAltEndingTests
{
    private const string ShaA = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

    private static ContentSyncStoryOptions Story(string id, string? altOf = null) => new()
    {
        StoryId = id,
        Sha256 = ShaA,
        SizeBytes = 100,
        AltOf = altOf ?? string.Empty,
    };

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    // ---- config binding ----

    /// <summary>KEYSTONE for the binding half: <c>Resolve</c> hand-rolls the
    /// story array and <c>ResolveStories</c> rebuilds each item field by
    /// field, so a field missing from EITHER loop binds as default and leaves
    /// every manifest test green while devices get no variant data.</summary>
    [Fact]
    public void Resolve_BindsAltOf_AndCarriesItThroughResolveStories()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "anban-huri-alt"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "100"),
            ("ContentSync:Stories:0:AltOf", "anban-huri")));

        Assert.Equal("anban-huri", Assert.Single(options.Stories).AltOf);
        Assert.Equal("anban-huri", Assert.Single(options.ResolveStories()).AltOf);
    }

    [Fact]
    public void Resolve_NoAltConfigured_BindsEmpty()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "anban-huri"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "100")));

        Assert.Equal(string.Empty, Assert.Single(options.Stories).AltOf);
    }

    // ---- manifest ----

    // KEYSTONE: a deployment shipping no variants has a byte-identical wire
    // shape to before this slice, so every toy already in the field sees no
    // change until the owner configures an alternate ending.
    [Fact]
    public void Manifest_NoAltConfigured_FieldStaysNull()
    {
        var options = new ContentSyncOptions { Enabled = true, Stories = { Story("anban-huri") } };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Null(story.AltOf);
    }

    [Fact]
    public void Manifest_ValidAltOf_IsCarried()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Story("anban-huri"), Story("anban-huri-alt", "anban-huri") },
        };

        var stories = new ContentManifestService(options).Build().Stories;

        Assert.Equal(2, stories.Count);
        Assert.Null(stories[0].AltOf);
        Assert.Equal("anban-huri", stories[1].AltOf);
    }

    [Fact]
    public void Manifest_AltOf_IsTrimmed()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Story("anban-huri-alt", "  anban-huri  ") },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Equal("anban-huri", story.AltOf);
    }

    // KEYSTONE, and the one rule that is INVERTED relative to the series
    // pairing. A malformed altOf drops the whole item rather than just the
    // field, because an alternate ending stripped of its altOf would enter the
    // device's rotation as an ordinary story — and a child would one day be
    // told half a story, starting mid-way, as though it were the whole thing.
    // The base story is untouched either way, so refusing the item costs
    // nothing a child would otherwise hear.
    [Theory]
    [InlineData("Anban-Huri")]        // uppercase — two SD-visible names
    [InlineData("anban-huri/../etc")] // outside the id allowlist
    [InlineData("anban huri")]        // space
    [InlineData("anban.huri")]        // '.' would reach an SD filename
    public void Manifest_MalformedAltOf_DropsTheWholeItem(string altOf)
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Story("anban-huri"), Story("anban-huri-alt", altOf) },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        // The variant is gone; the base story it belongs to is untouched.
        Assert.Equal("anban-huri", story.StoryId);
        Assert.Null(story.AltOf);
    }

    /// <summary>A self-referencing variant has no meaning — resolving it
    /// would hand back the very file the device was already about to play —
    /// so it is refused rather than shipped as a story of its own. Matched
    /// case-insensitively, mirroring the manifest's own id dedupe.</summary>
    [Theory]
    [InlineData("anban-huri")]
    [InlineData("ANBAN-HURI")]
    public void Manifest_SelfReferencingAltOf_DropsTheItem(string altOf)
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Story("anban-huri", altOf) },
        };

        Assert.Empty(new ContentManifestService(options).Build().Stories);
    }

    /// <summary>A dropped item must NOT consume its story id: a valid
    /// namesake later in the list still ships. This is why the altOf check
    /// runs ahead of the duplicate-id check.</summary>
    [Fact]
    public void Manifest_DroppedAltItem_DoesNotBlockAValidNamesake()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories =
            {
                Story("khosogh-dzuk", "Bad Id"),   // dropped
                Story("khosogh-dzuk"),             // the real story, same id
            },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Equal("khosogh-dzuk", story.StoryId);
        Assert.Null(story.AltOf);
    }

    /// <summary>A dangling reference (the named base story is not configured)
    /// is deliberately KEPT. The device fails closed — it only ever looks a
    /// variant up BY the base story it is already playing — so a dangling alt
    /// is inert, and rejecting it would make config edits order-dependent for
    /// no safety gain.</summary>
    [Fact]
    public void Manifest_DanglingAltOf_IsKept()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Story("khosogh-dzuk-alt", "khosogh-dzuk") },  // base absent
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Equal("khosogh-dzuk", story.AltOf);
    }

    /// <summary>Refusing one variant must not touch its siblings — the same
    /// per-item fail-closed discipline every other field on this manifest
    /// follows.</summary>
    [Fact]
    public void Manifest_OneBadVariant_KeepsTheOthers()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories =
            {
                Story("anban-huri"),
                Story("anban-huri-alt", "anban-huri"),
                Story("khosogh-dzuk-alt", "Bad Id"),     // dropped
                Story("little-cloud"),
            },
        };

        var stories = new ContentManifestService(options).Build().Stories;

        Assert.Equal(3, stories.Count);
        Assert.DoesNotContain(stories, s => s.StoryId == "khosogh-dzuk-alt");
        Assert.Equal("anban-huri", stories[1].AltOf);
    }

    /// <summary>The legacy flat-scalar config shape has no altOf at all, so a
    /// pre-multi-story overlay keeps producing exactly the wire it produced
    /// before this slice.</summary>
    [Fact]
    public void Manifest_LegacyScalarConfig_HasNoAltOf()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            StoryId = "anban-huri",
            Sha256 = ShaA,
            SizeBytes = 100,
        };

        Assert.Null(Assert.Single(new ContentManifestService(options).Build().Stories).AltOf);
    }

    /// <summary>A variant carries its own clips and version like any other
    /// entry — it is an ordinary ContentSync story in every respect except
    /// that the device never puts it in the rotation.</summary>
    [Fact]
    public void Manifest_Variant_GetsTheStandardDefaultAudioUrl()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Story("anban-huri-alt", "anban-huri") },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Equal("/api/devices/content-file?storyId=anban-huri-alt", story.AudioUrl);
    }

    // ---- the parent dashboard's half ----

    /// <summary>
    /// KEYSTONE for the dashboard counterpart. An alternate ending must not
    /// appear in the parent story library. It has no curated metadata of its
    /// own, so it would render as a card with a blank title, no author and no
    /// lesson — a phantom story a parent cannot make sense of, sitting beside
    /// the real ones. The variant belongs to its base story, not next to it.
    /// </summary>
    [Fact]
    public async Task StoryLibrary_ExcludesAlternateEndings()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Story("anban-huri"), Story("anban-huri-alt", "anban-huri") },
        };
        var manifest = Substitute.For<IContentManifestService>();
        manifest.Build().Returns(new ContentManifestService(options).Build());

        var parentService = Substitute.For<IParentService>();
        parentService.GetStoryPlayTotalsForParentAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new List<StoryPlayTotalDto>());

        var controller = new ParentController(parentService, new ExportCooldown());
        var user = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
            }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var ok = Assert.IsType<OkObjectResult>(
            await controller.GetStoryLibrary(Substitute.For<ICuratedStoryLibrary>(), manifest));

        var body = Assert.IsType<StoryLibraryResponse>(ok.Value);
        Assert.Equal("anban-huri", Assert.Single(body.Stories).StoryId);
    }
}
