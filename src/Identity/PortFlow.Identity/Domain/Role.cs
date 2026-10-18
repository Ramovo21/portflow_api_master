using PortFlow.Shared.Domain;

namespace PortFlow.Identity.Domain;

/// <summary>Role của app (= realm role Keycloak) → permission + phạm vi dữ liệu. Role system không sửa được qua API.</summary>
public sealed class Role
{
    private Role()
    {
    }

    public Role(string code, string name, IEnumerable<string> permissions, DataScope dataScope, bool system)
    {
        Code = code;
        Name = name;
        Permissions = permissions.ToArray();
        DataScope = dataScope;
        System = system;
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string[] Permissions { get; private set; } = [];

    public DataScope DataScope { get; private set; }

    public bool System { get; private set; }

    public long Version { get; private set; }

    public void Update(string name, IEnumerable<string> permissions, DataScope dataScope)
    {
        if (System)
        {
            throw new DomainException(AuthErrorCode.AUTH_SYSTEM_ROLE_IMMUTABLE);
        }
        Name = name;
        Permissions = permissions.ToArray();
        DataScope = dataScope;
    }
}
