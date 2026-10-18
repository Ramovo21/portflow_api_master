namespace PortFlow.Tests.Support.Security;

/// <summary>9 user mẫu – UUID trùng <c>db/seed</c> và realm Keycloak (<c>infra/keycloak/portflow-realm.json</c>).</summary>
public sealed record SeedUser(string Username, Guid Id, string FullName, string Email, string Department,
    IReadOnlyList<string> Roles)
{
    public static readonly Guid DEPT_KD = Guid.Parse("0192f0a0-0000-7000-8000-000000000001");
    public static readonly Guid DEPT_KT = Guid.Parse("0192f0a0-0000-7000-8000-000000000002");
    public static readonly Guid DEPT_PC = Guid.Parse("0192f0a0-0000-7000-8000-000000000004");
    public static readonly Guid DEPT_BGD = Guid.Parse("0192f0a0-0000-7000-8000-000000000005");

    public static readonly SeedUser SALES01 = New("sales01", 1, "Nguyễn Văn An", "an.nv@abc.vn", "KD", "SALES");
    public static readonly SeedUser SALESMGR = New("salesmgr", 2, "Trần Thị Bình", "binh.tt@abc.vn", "KD", "SALES_MANAGER");
    public static readonly SeedUser OPS01 = New("ops01", 3, "Lê Văn Cường", "cuong.lv@abc.vn", "KT", "OPERATIONS", "OPERATIONS_LEAD");
    public static readonly SeedUser ACC01 = New("acc01", 4, "Phạm Thị Dung", "dung.pt@abc.vn", "KTOAN", "ACCOUNTANT");
    public static readonly SeedUser CHIEFACC = New("chiefacc", 5, "Bùi Văn Khoa", "khoa.bv@abc.vn", "KTOAN", "CHIEF_ACCOUNTANT");
    public static readonly SeedUser LEGAL01 = New("legal01", 6, "Hoàng Văn Em", "em.hv@abc.vn", "PC", "LEGAL");
    public static readonly SeedUser DIRECTOR = New("director", 7, "Võ Thị Giang", "giang.vt@abc.vn", "BGD", "DIRECTOR");
    public static readonly SeedUser DIRECTOR2 = New("director2", 8, "Đỗ Văn Hải", "hai.dv@abc.vn", "BGD", "DIRECTOR");
    public static readonly SeedUser ADMIN = New("admin", 9, "Quản Trị", "admin@abc.vn", "IT", "ADMIN");

    public override string ToString() => Username;

    private static SeedUser New(string username, int n, string fullName, string email, string department,
        params string[] roles) =>
        new(username, Guid.Parse($"0192f0a1-0000-7000-8000-{n:000000000000}"), fullName, email, department, roles);
}
