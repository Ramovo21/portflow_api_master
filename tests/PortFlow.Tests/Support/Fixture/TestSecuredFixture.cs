using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Identity.Api;
using PortFlow.Shared.Security;

namespace PortFlow.Tests.Support.Fixture;

/// <summary>Endpoint giả để kiểm tra phân quyền 3 tầng (permission → scope theo phòng ban).</summary>
public static class TestSecuredFixture
{
    public const string ResourceType = "TEST_DOC";

    public static readonly ConcurrentDictionary<Guid, Guid> Owners = new();

    public static void Register(IServiceCollection services) =>
        services.AddSingleton<IScopeResolver, TestDocScopeResolver>();

    private sealed class TestDocScopeResolver : IScopeResolver
    {
        public string ResourceType => TestSecuredFixture.ResourceType;

        public Task<Guid?> OwnerDepartmentOfAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Owners.TryGetValue(id, out var owner) ? owner : (Guid?)null);
    }
}

[ApiController]
[Route("test/secured")]
public sealed class TestSecuredController : ControllerBase
{
    [HttpGet("docs/{id:guid}")]
    [RequirePermission(Permission.CONTRACT_READ)]
    [RequireScope(TestSecuredFixture.ResourceType)]
    public Dictionary<string, object> Read(Guid id) => new() { ["id"] = id };

    [HttpPost("steps/{stepId:guid}/approve")]
    [RequirePermission(Permission.WORKFLOW_ACT)]
    public Dictionary<string, object> Approve(Guid stepId) => new() { ["approved"] = stepId };
}
