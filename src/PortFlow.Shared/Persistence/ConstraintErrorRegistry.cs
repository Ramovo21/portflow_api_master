using PortFlow.Shared.Domain;

namespace PortFlow.Shared.Persistence;

/// <summary>Module đăng ký tên constraint DB → mã lỗi của mình (vd <c>"price_list_no_overlap" → PRC_OVERLAP</c>).</summary>
public interface IConstraintErrorMapping
{
    IReadOnlyDictionary<string, IErrorCode> Mappings { get; }
}

public sealed class ConstraintErrorRegistry
{
    private readonly Dictionary<string, IErrorCode> _mappings = new();

    public ConstraintErrorRegistry(IEnumerable<IConstraintErrorMapping> modules)
    {
        foreach (var module in modules)
        {
            foreach (var (constraint, code) in module.Mappings)
            {
                if (!_mappings.TryAdd(constraint, code) && !Equals(_mappings[constraint], code))
                {
                    throw new InvalidOperationException(
                        $"Constraint '{constraint}' mapped twice: {_mappings[constraint].Code} and {code.Code}");
                }
            }
        }
    }

    public IErrorCode? Lookup(string? constraintName) =>
        constraintName is not null && _mappings.TryGetValue(constraintName, out var code) ? code : null;
}
