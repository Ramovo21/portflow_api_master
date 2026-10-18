using PortFlow.Shared.Id;

namespace PortFlow.Tests.Shared.Id;

public class UuidV7Tests
{
    [Fact]
    public void IsVersion7WithRfcVariant()
    {
        var id = UuidV7.Next().ToString();
        Assert.Equal('7', id[14]);
        Assert.Contains(id[19], "89ab");
    }

    [Fact]
    public void IsStrictlyIncreasingAndUnique()
    {
        var ids = Enumerable.Range(0, 20_000).Select(_ => UuidV7.Next()).ToList();
        for (var i = 1; i < ids.Count; i++)
        {
            Assert.True(string.CompareOrdinal(ids[i].ToString(), ids[i - 1].ToString()) > 0);
            Assert.True(ids[i].CompareTo(ids[i - 1]) > 0);
        }
        Assert.Equal(ids.Count, ids.ToHashSet().Count);
    }

    [Fact]
    public void EmbedsCurrentTimestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var millis = Convert.ToInt64(UuidV7.Next().ToString("N")[..12], 16);
        Assert.InRange(millis, before - 1_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1_000);
    }
}
