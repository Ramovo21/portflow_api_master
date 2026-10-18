using System.Text.RegularExpressions;

namespace PortFlow.Shared.Web;

/// <summary>Correlation-id của luồng hiện tại (= MDC "correlationId"), sống theo async flow.</summary>
public static partial class CorrelationId
{
    public const string Header = "X-Correlation-Id";
    public const string LogKey = "correlationId";

    private static readonly AsyncLocal<string?> Value = new();

    public static string? Current => Value.Value;

    public static string CurrentOrNew() => Current ?? Guid.NewGuid().ToString();

    /// <summary>Đặt correlation-id mới, trả về giá trị cũ để khôi phục.</summary>
    public static string? Set(string? cid)
    {
        var previous = Current;
        Value.Value = cid;
        return previous;
    }

    internal static string Sanitize(string? candidate) =>
        candidate is not null && Valid().IsMatch(candidate) ? candidate : Guid.NewGuid().ToString();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex Valid();
}
