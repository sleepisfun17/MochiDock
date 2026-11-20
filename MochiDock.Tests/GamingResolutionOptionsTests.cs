using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class GamingResolutionOptionsTests
{
    static readonly (int, int)[] Modes =
    {
        (2560, 1440), (2304, 1440), (1920, 1080), (1728, 1080), (1600, 900), (1280, 720), (1024, 768), (800, 600), (640, 480), (2560, 1440),
    };

    [Fact]
    public void BuildsSizeAndShapeFromTheModesTheDisplayOffers()
    {
        var options = GamingResolutionOptions.Build(Modes);
        Assert.Contains(options, o => o.SizeName == "QHD" && o.Aspect == "16:9" && o.Value == "2560x1440");
        Assert.Contains(options, o => o.SizeName == "FHD" && o.Aspect == "16:10" && o.Value == "1728x1080");
        Assert.DoesNotContain(options, o => o.Height < 720);                       // 800x600 and 640x480 are no gaming sizes
        Assert.Equal(options.Count, options.Select(o => o.Value).Distinct().Count()); // refresh-rate duplicates collapse
    }

    [Fact]
    public void SizesAreTallestFirstAndShapesFollowTheFixedOrder()
    {
        var options = GamingResolutionOptions.Build(Modes);
        Assert.Equal(new[] { 1440, 1080, 900, 768, 720 }, GamingResolutionOptions.Sizes(options).Select(s => s.TierHeight));
        Assert.Equal(new[] { "16:9", "16:10" }, GamingResolutionOptions.AspectsFor(options, 1080).Select(o => o.Aspect).Take(2));
    }

    [Fact]
    public void OnlyListsCombinationsTheDisplayHas()
    {
        var options = GamingResolutionOptions.Build(new (int, int)[] { (1920, 1080), (1280, 720) });
        Assert.Empty(GamingResolutionOptions.AspectsFor(options, 1080).Where(o => o.Aspect == "16:10"));
        Assert.Null(GamingResolutionOptions.Find(options, "1728x1080"));
    }

    [Theory]
    [InlineData(1080, "FHD")]
    [InlineData(1440, "QHD")]
    [InlineData(2160, "4K")]
    [InlineData(1000, "1000p")]
    public void NamesTheSize(int height, string name) => Assert.Equal(name, GamingResolutionOptions.SizeName(height));

    [Fact]
    public void FindsAStoredValueInAnyWritingAndSuggestsTheStretchedShape()
    {
        var options = GamingResolutionOptions.Build(Modes);
        Assert.Equal("2304x1440", GamingResolutionOptions.Find(options, "2304×1440")?.Value);
        Assert.Equal("2304x1440", GamingResolutionOptions.SuggestStretched(options, (2560, 1440))?.Value);
        Assert.Null(GamingResolutionOptions.SuggestStretched(GamingResolutionOptions.Build(new (int, int)[] { (1920, 1080) }), (1920, 1080)));
    }

    [Fact]
    public void ResolvesTheStoredValueOnlyWhenTheDisplayOffersIt()
    {
        var qhd = GamingResolutionOptions.Build(Modes);
        Assert.Equal("1728x1080", GamingResolutionOptions.Resolve(qhd, (2560, 1440), "1728x1080")?.Value);
        Assert.Null(GamingResolutionOptions.Resolve(qhd, (2560, 1440), "1680x1050"));            // stored but not offered: no switch

        var fhd = GamingResolutionOptions.Build(new (int, int)[] { (1920, 1080), (1680, 1050), (1280, 720) });
        Assert.Null(GamingResolutionOptions.Resolve(fhd, (1920, 1080), "2304x1440"));            // the old default on a 1080p display
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NewInstallsGetTheStretchedSuggestionWhenTheDisplayHasOne(string? stored)
    {
        Assert.Equal("2304x1440", GamingResolutionOptions.Resolve(GamingResolutionOptions.Build(Modes), (2560, 1440), stored)?.Value);
        Assert.Null(GamingResolutionOptions.Resolve(GamingResolutionOptions.Build(new (int, int)[] { (1920, 1080), (1280, 720) }), (1920, 1080), stored));
    }
}
