namespace PortFlow.Shared.Security;

public interface ICurrentUser
{
    Guid Id { get; }

    string Username { get; }

    string FullName { get; }

    Guid? DepartmentId { get; }

    IReadOnlySet<string> Roles { get; }

    IReadOnlySet<string> Permissions { get; }

    bool IsSystem => false;
}
