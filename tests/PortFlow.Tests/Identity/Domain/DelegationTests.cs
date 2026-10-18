using NodaTime;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Domain;

namespace PortFlow.Tests.Identity.Domain;

public class DelegationTests
{
    private static readonly LocalDate Today = new(2026, 10, 13);
    private static readonly Guid From = Guid.NewGuid();
    private static readonly Guid To = Guid.NewGuid();

    private static Delegation Grant(Guid? to, LocalDate from, LocalDate until, string reason) =>
        Delegation.Grant(Guid.NewGuid(), From, to, ["CONTRACT"], from, until, reason, Today);

    [Fact]
    public void GrantTrimsReasonAndCoversOnlyItsTypesAndPeriod()
    {
        var delegation = Grant(To, Today, Today.PlusDays(2), "  Nghỉ phép ");
        Assert.Equal("Nghỉ phép", delegation.Reason);
        Assert.True(delegation.Covers("CONTRACT", Today.PlusDays(2)));
        Assert.False(delegation.Covers("CONTRACT", Today.PlusDays(3)));
        Assert.False(delegation.Covers("PAYMENT_SHEET", Today));
    }

    [Fact]
    public void EmptyDocumentTypesCoverEveryType()
    {
        var delegation = Delegation.Grant(Guid.NewGuid(), From, To, null, Today, Today, "Công tác", Today);
        Assert.Empty(delegation.DocumentTypes);
        Assert.True(delegation.Covers("PRICE_LIST", Today));
    }

    [Fact]
    public void RejectsInvalidGrants()
    {
        AssertInvalid(() => Grant(null, Today, Today, "x"), "Missing");
        AssertInvalid(() => Grant(To, Today, Today, "  "), "Missing");
        AssertInvalid(() => Grant(From, Today, Today, "x"), "yourself");
        AssertInvalid(() => Grant(To, Today.PlusDays(2), Today, "x"), "Start date");
        AssertInvalid(() => Grant(To, Today.PlusDays(-5), Today.PlusDays(-1), "x"), "already passed");
    }

    [Fact]
    public void PeriodEndingTodayIsStillAllowed()
    {
        Assert.True(Grant(To, Today.PlusDays(-3), Today, "x").Covers("CONTRACT", Today));
    }

    [Fact]
    public void RevokeIsIdempotentAndStopsCoverage()
    {
        var delegation = Grant(To, Today, Today, "x");
        Assert.True(delegation.Revoke());
        Assert.False(delegation.Revoke());
        Assert.False(delegation.Covers("CONTRACT", Today));
    }

    private static void AssertInvalid(Action call, string message)
    {
        var ex = Assert.Throws<DomainException>(call);
        Assert.Equal(SysErrorCode.SYS_VALIDATION, ex.Code);
        Assert.Contains(message, ex.Message);
    }
}
