namespace PortFlow.Shared.Web;

/// <summary>Định dạng phân trang chuẩn: <c>{ items, total, page, size }</c> (page bắt đầu từ 0).</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, long Total, int Page, int Size)
{
    public static PageResponse<T> Of(IReadOnlyList<T> items, long total, int page, int size) =>
        new(items, total, page, size);

    public static PageResponse<T> Of<TSource>(IReadOnlyList<TSource> items, long total, int page, int size,
        Func<TSource, T> mapper) =>
        new(items.Select(mapper).ToList(), total, page, size);
}
