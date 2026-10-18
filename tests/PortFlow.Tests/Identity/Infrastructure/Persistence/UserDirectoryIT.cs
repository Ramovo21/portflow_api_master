using Microsoft.Extensions.DependencyInjection;
using PortFlow.Identity.Api;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Infrastructure.Persistence;

public class UserDirectoryIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task FindsSeededUserWhoNeverLoggedIn()
    {
        var user = await Directory(d => d.FindAsync(SeedUser.LEGAL01.Id));
        Assert.NotNull(user);
        Assert.Equal("legal01", user.Username);
        Assert.Equal("Hoàng Văn Em", user.FullName);
        Assert.Equal(SeedUser.DEPT_PC, user.DepartmentId);
        Assert.Equal("em.hv@abc.vn", user.Email);
        Assert.Null(await Directory(d => d.FindAsync(Guid.NewGuid())));
    }

    [Fact]
    public async Task FindsActiveUsersByRole()
    {
        var directors = (await Directory(d => d.FindActiveByRoleAsync("DIRECTOR"))).Select(u => u.Id).ToList();
        Assert.Contains(SeedUser.DIRECTOR.Id, directors);
        Assert.Contains(SeedUser.DIRECTOR2.Id, directors);
        Assert.DoesNotContain(SeedUser.DIRECTOR.Id, (await Directory(d => d.FindActiveByRoleAsync("ADMIN"))).Select(u => u.Id));
    }

    [Fact]
    public async Task FindsActiveUsersByRoleInDepartment()
    {
        Assert.Contains(SeedUser.SALES01.Id,
            (await Directory(d => d.FindActiveByRoleInDepartmentAsync("SALES", SeedUser.DEPT_KD))).Select(u => u.Id));
        Assert.Empty(await Directory(d => d.FindActiveByRoleInDepartmentAsync("SALES", SeedUser.DEPT_PC)));
    }

    [Fact]
    public async Task ResolvesHeadOfDepartment()
    {
        Assert.Equal(SeedUser.SALESMGR.Id, (await Directory(d => d.HeadOfDepartmentAsync(SeedUser.DEPT_KD)))?.Id);
        Assert.Equal(SeedUser.SALESMGR.Id, (await Directory(d => d.HeadOfDepartmentOfAsync(SeedUser.SALES01.Id)))?.Id);
        Assert.Null(await Directory(d => d.HeadOfDepartmentAsync(SeedUser.DEPT_PC)));
    }

    [Fact]
    public async Task ReportsActivityAndNames()
    {
        Assert.True(await Directory(d => d.IsActiveAsync(SeedUser.ACC01.Id)));
        Assert.False(await Directory(d => d.IsActiveAsync(Guid.NewGuid())));
        var names = await Directory(d => d.FullNamesAsync([SeedUser.SALES01.Id, SeedUser.DIRECTOR.Id, SeedUser.SALES01.Id]));
        Assert.Equal(2, names.Count);
        Assert.Equal("Nguyễn Văn An", names[SeedUser.SALES01.Id]);
        Assert.Equal("Võ Thị Giang", names[SeedUser.DIRECTOR.Id]);
        Assert.Empty(await Directory(d => d.FullNamesAsync([])));
    }

    [Fact]
    public async Task ExposesDepartmentCodeAndRolePermissions()
    {
        Assert.Equal("KD", await Directory(d => d.DepartmentCodeOfAsync(SeedUser.SALES01.Id)));
        Assert.Contains("Contract.Submit", (await Directory(d => d.PermissionsOfRoleAsync("SALES")))!);
        Assert.Null(await Directory(d => d.PermissionsOfRoleAsync("NOPE")));
    }

    private async Task<T> Directory<T>(Func<IUserDirectory, Task<T>> query)
    {
        await using var scope = Fixture.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IUserDirectory>());
    }
}
