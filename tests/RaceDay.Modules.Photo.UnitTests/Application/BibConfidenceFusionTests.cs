using RaceDay.Modules.Photo.Application.Photos;

namespace RaceDay.Modules.Photo.UnitTests.Application;

public class BibConfidenceFusionTests
{
    [Fact]
    public void Fuse_Weights_Model_70_Match_30_But_Match_Cannot_Inflate_Past_Model()
    {
        // weighted 0.7*0.80+0.3*1.0=0.86, capped at model+0.04 → 0.84, soft-cap 0.95
        Assert.Equal(0.84, BibConfidenceFusion.Fuse(0.80, 1.0, ambiguous: false));
    }

    [Fact]
    public void Fuse_Never_Reports_Near_Perfect_99()
    {
        // Paddle overconfident + exact roster must not become 0.99
        var fused = BibConfidenceFusion.Fuse(0.98, 1.0, ambiguous: false);
        Assert.True(fused <= BibConfidenceFusion.ReportSoftCap);
        Assert.True(fused < 0.99);
    }

    [Fact]
    public void Exact_Roster_Match_Score_Is_Full()
    {
        var roster = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "50001" };
        var hit = BibConfidenceFusion.FindBest("50001", roster);
        Assert.NotNull(hit);
        Assert.Equal("50001", hit!.Value.RosterBib);
        Assert.Equal(1.0, hit.Value.MatchScore);
        Assert.False(hit.Value.Ambiguous);
    }

    [Fact]
    public void Partial_Suffix_Goes_To_Review_Band_Not_Auto_Match()
    {
        var roster = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "50001" };
        var hit = BibConfidenceFusion.FindBest("0001", roster);
        Assert.NotNull(hit);
        Assert.Equal("50001", hit!.Value.RosterBib);
        Assert.True(hit.Value.MatchScore is >= 0.70 and < 0.95);
    }

    [Fact]
    public void Unrelated_Fragment_Does_Not_Match()
    {
        var roster = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "50001" };
        Assert.Null(BibConfidenceFusion.FindBest("60389", roster));
        Assert.Null(BibConfidenceFusion.FindBest("992", roster));
    }

    [Fact]
    public void Ambiguous_When_Two_Roster_Bibs_Close()
    {
        var roster = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "50001", "50002" };
        // "5000" is close to both — expect ambiguous or weak
        var hit = BibConfidenceFusion.FindBest("5000", roster);
        if (hit is not null)
        {
            Assert.True(hit.Value.Ambiguous || hit.Value.MatchScore < 0.95);
        }
    }
}
