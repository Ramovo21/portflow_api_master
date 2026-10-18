namespace PortFlow.Shared.Events;

/// <summary>
/// Đánh dấu event được đẩy ra RabbitMQ sau commit (= <c>@Externalized</c> của Spring Modulith).
/// Cú pháp đích: <c>"&lt;exchange&gt;::&lt;routing-key&gt;"</c>, vd <c>"portflow.events::workflow.document-approved"</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExternalizedAttribute : Attribute
{
    public ExternalizedAttribute(string target)
    {
        var parts = target.Split("::", 2);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ArgumentException($"Externalized target must be '<exchange>::<routing-key>': {target}");
        }
        Target = target;
        Exchange = parts[0];
        RoutingKey = parts[1];
    }

    public string Target { get; }

    public string Exchange { get; }

    public string RoutingKey { get; }
}
