using System.Globalization;
using System.Text.RegularExpressions;

namespace PortFlow.Shared.Domain;

public sealed partial record Money : IComparable<Money>
{
    public const int Scale = 2;

    public Money(decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (!CurrencyCode().IsMatch(currency))
        {
            throw new ArgumentException($"Invalid ISO 4217 currency code: {currency}", nameof(currency));
        }
        Amount = Round(amount);
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Of(string amount, string currency) =>
        new(decimal.Parse(amount, NumberStyles.Number, CultureInfo.InvariantCulture), currency);

    public static Money Of(decimal amount, string currency) => new(amount, currency);

    public static Money Zero(string currency) => new(0m, currency);

    public Money Plus(Money other)
    {
        RequireSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Minus(Money other)
    {
        RequireSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Times(decimal factor) => new(Amount * factor, Currency);

    public Money Negate() => new(-Amount, Currency);

    public bool IsNegative => Amount < 0;

    public bool IsZero => Amount == 0;

    public int CompareTo(Money? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        RequireSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString() => Amount.ToString(CultureInfo.InvariantCulture) + " " + Currency;

    // HALF_UP của Java = AwayFromZero; cộng 0.00m để luôn giữ đúng 2 chữ số thập phân như setScale(2).
    private static decimal Round(decimal value) =>
        decimal.Round(value, Scale, MidpointRounding.AwayFromZero) + 0.00m;

    private void RequireSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new DomainException(SysErrorCode.SYS_CURRENCY_MISMATCH,
                $"Cannot calculate between {Currency} and {other.Currency}");
        }
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
