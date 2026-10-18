using PortFlow.Shared.Domain;

namespace PortFlow.Tests.Shared.Domain;

public class MoneyTests
{
    [Fact]
    public void RoundsHalfUpToTwoDecimals()
    {
        Assert.Equal(10.01m, Money.Of("10.005", "VND").Amount);
        Assert.Equal(10.00m, Money.Of("10.004", "VND").Amount);
        Assert.Equal(-10.01m, Money.Of("-10.005", "VND").Amount);
    }

    [Fact]
    public void KeepsScaleTwoLikeJavaSetScale()
    {
        Assert.Equal("5.00 VND", Money.Of("5", "VND").ToString());
    }

    [Fact]
    public void TimesQuantityRoundsOnceOnTheResult()
    {
        var unitPrice = Money.Of("1250000.00", "VND");
        Assert.Equal(4166250.00m, unitPrice.Times(3.333m).Amount);
        Assert.Equal(0.05m, Money.Of("0.15", "VND").Times(0.333m).Amount);
    }

    [Fact]
    public void AddsAndSubtractsSameCurrency()
    {
        var a = Money.Of("100.10", "VND");
        var b = Money.Of("0.20", "VND");
        Assert.Equal(Money.Of("100.30", "VND"), a.Plus(b));
        Assert.True(b.Minus(a).IsNegative);
        Assert.True(a.Minus(a).IsZero);
    }

    [Fact]
    public void RejectsArithmeticAcrossCurrencies()
    {
        var ex = Assert.Throws<DomainException>(() => Money.Of("1", "VND").Plus(Money.Of("1", "USD")));
        Assert.Equal(SysErrorCode.SYS_CURRENCY_MISMATCH, ex.Code);
    }

    [Fact]
    public void EqualityIgnoresInputScale()
    {
        Assert.Equal(Money.Of("5", "VND"), Money.Of("5.000", "VND"));
    }
}
