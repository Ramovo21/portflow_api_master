using NodaTime.Text;
using PortFlow.Shared.Domain;

namespace PortFlow.Tests.Shared.Domain;

public class DateRangeTests
{
    private static NodaTime.LocalDate D(string iso) => LocalDatePattern.Iso.Parse(iso).Value;

    private readonly DateRange _october = DateRange.Of(D("2026-10-01"), D("2026-10-31"));

    [Fact]
    public void BoundsAreInclusive()
    {
        Assert.True(_october.Contains(D("2026-10-01")));
        Assert.True(_october.Contains(D("2026-10-31")));
        Assert.False(_october.Contains(D("2026-11-01")));
        Assert.Equal(31, _october.Days());
    }

    [Fact]
    public void TouchingRangesOverlapBecauseBoundsAreInclusive()
    {
        Assert.True(_october.Overlaps(DateRange.Of(D("2026-10-31"), D("2026-11-30"))));
        Assert.False(_october.Overlaps(DateRange.Of(D("2026-11-01"), D("2026-11-30"))));
    }

    [Fact]
    public void OpenEndedRangeOverlapsEverythingAfterStart()
    {
        var fromNovember = DateRange.OpenEnded(D("2026-11-01"));
        Assert.True(fromNovember.Contains(D("2099-01-01")));
        Assert.False(fromNovember.Overlaps(_october));
        Assert.True(DateRange.OpenEnded(D("2026-10-15")).Overlaps(_october));
    }

    [Fact]
    public void IntersectReturnsCommonPart()
    {
        Assert.Equal(DateRange.Of(D("2026-10-15"), D("2026-10-31")),
            _october.Intersect(DateRange.OpenEnded(D("2026-10-15"))));
        Assert.Equal(DateRange.OpenEnded(D("2026-06-01")),
            DateRange.OpenEnded(D("2026-01-01")).Intersect(DateRange.OpenEnded(D("2026-06-01"))));
        Assert.Null(_october.Intersect(DateRange.Of(D("2026-12-01"), D("2026-12-31"))));
    }

    [Fact]
    public void SplitAtMidPeriodForAddendumEffectiveOnThe15th()
    {
        Assert.Equal(
            [DateRange.Of(D("2026-10-01"), D("2026-10-14")), DateRange.Of(D("2026-10-15"), D("2026-10-31"))],
            _october.SplitAt(D("2026-10-15")));
        Assert.Equal([_october], _october.SplitAt(D("2026-10-01")));
        Assert.Equal([_october], _october.SplitAt(D("2026-12-01")));
    }

    [Fact]
    public void RejectsInvertedRange()
    {
        Assert.Throws<ArgumentException>(() => DateRange.Of(D("2026-10-02"), D("2026-10-01")));
    }

    [Fact]
    public void PrintsLikePostgresInclusiveRange()
    {
        Assert.Equal("[2026-10-01, 2026-10-31]", _october.ToString());
        Assert.Equal("[2026-11-01, ∞]", DateRange.OpenEnded(D("2026-11-01")).ToString());
    }
}
