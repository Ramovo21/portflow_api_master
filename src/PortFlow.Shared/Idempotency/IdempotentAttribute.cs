namespace PortFlow.Shared.Idempotency;

/// <summary>
/// Đặt trên action POST đổi trạng thái: bắt buộc header <c>Idempotency-Key</c> (UUID), lưu key cùng transaction
/// với use case và phát lại response cho request trùng (QĐ-06). Action phải trả <c>IActionResult</c>/<c>ActionResult&lt;T&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
}
