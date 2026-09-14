namespace BrunoVehicleHire.Domain.Tests;

/// <summary>
/// Full overlap/non-overlap/touching-endpoint/identical-range matrix for
/// <see cref="DateRange.Overlaps"/> (spec-4-2) -- a pure, unit-tested domain method with zero
/// infrastructure dependencies, written test-first before <see cref="DateRange"/> existed.
/// </summary>
public class DateRangeTests
{
    private static DateRange Range(int startDay, int endDay) =>
        new(new DateOnly(2026, 9, startDay), new DateOnly(2026, 9, endDay));

    [Fact]
    public void Overlaps_IdenticalRanges_ReturnsTrue()
    {
        var a = Range(2, 4);
        var b = Range(2, 4);

        Assert.True(a.Overlaps(b));
    }

    [Fact]
    public void Overlaps_PartiallyOverlappingRanges_ReturnsTrue()
    {
        // 2-4 Sep and 3-5 Sep share 3-4 Sep.
        var a = Range(2, 4);
        var b = Range(3, 5);

        Assert.True(a.Overlaps(b));
        Assert.True(b.Overlaps(a));
    }

    [Fact]
    public void Overlaps_OneRangeFullyContainsTheOther_ReturnsTrue()
    {
        var outer = Range(1, 10);
        var inner = Range(3, 5);

        Assert.True(outer.Overlaps(inner));
        Assert.True(inner.Overlaps(outer));
    }

    [Fact]
    public void Overlaps_CompletelySeparateRanges_ReturnsFalse()
    {
        var a = Range(1, 3);
        var b = Range(10, 12);

        Assert.False(a.Overlaps(b));
        Assert.False(b.Overlaps(a));
    }

    [Fact]
    public void Overlaps_ThisRangesEndEqualsOthersStart_ReturnsFalse_SameDayTurnover()
    {
        // 2-4 Sep, then 4-6 Sep: End (4) touches other's Start (4) -- not an overlap.
        var a = Range(2, 4);
        var b = Range(4, 6);

        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void Overlaps_OthersEndEqualsThisRangesStart_ReturnsFalse_SameDayTurnover()
    {
        // The symmetric touching case: b ends exactly when a starts.
        var a = Range(4, 6);
        var b = Range(2, 4);

        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void Overlaps_AdjacentNonTouchingRanges_ReturnsFalse()
    {
        var a = Range(1, 4);
        var b = Range(5, 8);

        Assert.False(a.Overlaps(b));
        Assert.False(b.Overlaps(a));
    }
}
