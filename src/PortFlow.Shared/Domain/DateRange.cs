using NodaTime;
using NodaTime.Text;

namespace PortFlow.Shared.Domain;

/// <summary>Khoảng ngày bao gồm hai đầu <c>[]</c>; <see cref="ToInclusive"/> = null là vô hạn (khớp <c>daterange(..., '[]')</c>).</summary>
public sealed record DateRange
{
    public DateRange(LocalDate from, LocalDate? toInclusive)
    {
        if (toInclusive is { } to && to < from)
        {
            throw new ArgumentException($"DateRange from {Iso(from)} is after to {Iso(to)}");
        }
        From = from;
        ToInclusive = toInclusive;
    }

    public LocalDate From { get; }

    public LocalDate? ToInclusive { get; }

    public static DateRange Of(LocalDate from, LocalDate? toInclusive) => new(from, toInclusive);

    public static DateRange OpenEnded(LocalDate from) => new(from, null);

    public bool IsOpenEnded => ToInclusive is null;

    public bool Contains(LocalDate date) => date >= From && (ToInclusive is null || date <= ToInclusive);

    public bool Overlaps(DateRange other) => !EndsBefore(other.From) && !other.EndsBefore(From);

    public DateRange? Intersect(DateRange other)
    {
        if (!Overlaps(other))
        {
            return null;
        }
        var start = From > other.From ? From : other.From;
        LocalDate? end;
        if (ToInclusive is null)
        {
            end = other.ToInclusive;
        }
        else if (other.ToInclusive is null)
        {
            end = ToInclusive;
        }
        else
        {
            end = ToInclusive < other.ToInclusive ? ToInclusive : other.ToInclusive;
        }
        return new DateRange(start, end);
    }

    public IReadOnlyList<DateRange> SplitAt(LocalDate date)
    {
        if (!Contains(date) || date == From)
        {
            return [this];
        }
        return [new DateRange(From, date.PlusDays(-1)), new DateRange(date, ToInclusive)];
    }

    public long Days()
    {
        if (ToInclusive is not { } to)
        {
            throw new InvalidOperationException("Open-ended range has no length");
        }
        return Period.DaysBetween(From, to) + 1;
    }

    public override string ToString() =>
        "[" + Iso(From) + ", " + (ToInclusive is { } to ? Iso(to) : "∞") + "]";

    private bool EndsBefore(LocalDate date) => ToInclusive is { } to && to < date;

    private static string Iso(LocalDate date) => LocalDatePattern.Iso.Format(date);
}
