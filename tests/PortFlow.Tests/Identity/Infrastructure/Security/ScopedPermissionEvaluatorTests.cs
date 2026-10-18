using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PortFlow.Identity.Api;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Identity.Infrastructure.Security;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Security;

namespace PortFlow.Tests.Identity.Infrastructure.Security;

public class ScopedPermissionEvaluatorTests
{
    private readonly Guid _kd = Guid.NewGuid();
    private readonly Guid _kd2 = Guid.NewGuid();
    private readonly Guid _ownedByKd = Guid.NewGuid();
    private readonly Guid _ownedByKd2 = Guid.NewGuid();
    private readonly IRolePermissions _roles = Substitute.For<IRolePermissions>();
    private readonly ScopedPermissionEvaluator _evaluator;

    public ScopedPermissionEvaluatorTests()
    {
        _evaluator = new ScopedPermissionEvaluator([new ContractResolver(this)], _roles,
            NullLogger<ScopedPermissionEvaluator>.Instance);
    }

    [Fact]
    public async Task DepartmentScopeAllowsOwnDepartmentWithSpecSyntax()
    {
        Scope(DataScope.DEPARTMENT);
        Assert.True(await _evaluator.HasPermissionAsync(User("SALES"), _ownedByKd, "CONTRACT"));
        Assert.True(await _evaluator.HasPermissionAsync(User("SALES"), _ownedByKd.ToString(), "CONTRACT"));
    }

    [Fact]
    public async Task DepartmentScopeRejectsOtherDepartment()
    {
        Scope(DataScope.DEPARTMENT);
        Assert.False(await _evaluator.HasPermissionAsync(User("SALES"), _ownedByKd2, "CONTRACT"));
    }

    [Fact]
    public async Task AllScopeAllowsAnyDepartment()
    {
        Scope(DataScope.ALL);
        Assert.True(await _evaluator.HasPermissionAsync(User("LEGAL"), _ownedByKd2, "CONTRACT"));
    }

    [Fact]
    public async Task UnknownResourceTypeOrNonUuidIsDenied()
    {
        Scope(DataScope.ALL);
        Assert.False(await _evaluator.HasPermissionAsync(User("LEGAL"), _ownedByKd, "INVOICE"));
        Assert.False(await _evaluator.HasPermissionAsync(User("LEGAL"), "not-a-uuid", "CONTRACT"));
    }

    [Fact]
    public async Task MissingRecordIsNotFound()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _evaluator.HasPermissionAsync(User("SALES"), Guid.NewGuid(), "CONTRACT"));
        Assert.Contains("Record not found", ex.Message);
    }

    [Fact]
    public async Task AnonymousAndSystemAreDenied()
    {
        Assert.False(await _evaluator.HasPermissionAsync(null, _ownedByKd, "CONTRACT"));
        Assert.False(await _evaluator.HasPermissionAsync(SystemUser.Instance, _ownedByKd, "CONTRACT"));
    }

    private void Scope(DataScope scope) =>
        _roles.DataScopeOfAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(scope);

    private TokenCurrentUser User(string role) =>
        new(Guid.NewGuid(), "u", "U", _kd, new HashSet<string> { role }, new HashSet<string> { "Contract.Read" },
            DataScope.DEPARTMENT, false);

    private sealed class ContractResolver(ScopedPermissionEvaluatorTests test) : IScopeResolver
    {
        public string ResourceType => "CONTRACT";

        public Task<Guid?> OwnerDepartmentOfAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(id == test._ownedByKd ? test._kd : id == test._ownedByKd2 ? test._kd2 : (Guid?)null);
    }
}
