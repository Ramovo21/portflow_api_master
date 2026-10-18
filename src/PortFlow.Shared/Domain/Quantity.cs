using System.Globalization;

namespace PortFlow.Shared.Domain;

public sealed record Quantity : IComparable<Quantity>
{
    public const int Scale = 3;

    public Quantity(decimal value)
    {
        Value = decimal.Round(value, Scale, MidpointRounding.AwayFromZero) + 0.000m;
    }

    public decimal Value { get; }

    public static Quantity Of(string value) =>
        Of(decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture));

    public static Quantity Of(decimal value)
    {
        if (value < 0)
        {
            throw new ArgumentException($"Quantity must not be negative: {value}", nameof(value));
        }
        return new Quantity(value);
    }

    public static Quantity Signed(decimal value) => new(value);

    public static Quantity Zero() => new(0m);

    public Quantity Plus(Quantity other) => new(Value + other.Value);

    public bool IsNegative => Value < 0;

    public int CompareTo(Quantity? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Value.CompareTo(other.Value);
    }
}
