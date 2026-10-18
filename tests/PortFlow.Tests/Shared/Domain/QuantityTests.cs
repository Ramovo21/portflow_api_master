using PortFlow.Shared.Domain;

namespace PortFlow.Tests.Shared.Domain;

public class QuantityTests
{
    [Fact]
    public void KeepsThreeDecimals()
    {
        Assert.Equal(1.235m, Quantity.Of("1.23456").Value);
    }

    [Fact]
    public void RejectsNegativeUnlessSigned()
    {
        Assert.Throws<ArgumentException>(() => Quantity.Of("-1"));
        Assert.True(Quantity.Signed(-1m).IsNegative);
    }
}
