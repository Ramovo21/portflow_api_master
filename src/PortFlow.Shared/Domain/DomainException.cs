namespace PortFlow.Shared.Domain;

public class DomainException : Exception
{
    public DomainException(IErrorCode code)
        : this(code, code.Title, new Dictionary<string, object?>())
    {
    }

    public DomainException(IErrorCode code, string detail)
        : this(code, detail, new Dictionary<string, object?>())
    {
    }

    public DomainException(IErrorCode code, string detail, IReadOnlyDictionary<string, object?> args)
        : base(detail)
    {
        Code = code;
        Args = new Dictionary<string, object?>(args);
    }

    public IErrorCode Code { get; }

    public IReadOnlyDictionary<string, object?> Args { get; }
}
