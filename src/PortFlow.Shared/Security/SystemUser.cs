namespace PortFlow.Shared.Security;

public sealed class SystemUser : ICurrentUser
{
    public static readonly Guid SystemId = Guid.Parse("00000000-0000-7000-8000-000000000001");
    public static readonly SystemUser Instance = new();

    private SystemUser()
    {
    }

    public Guid Id => SystemId;

    public string Username => "system";

    public string FullName => "System";

    public Guid? DepartmentId => null;

    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();

    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

    public bool IsSystem => true;
}
