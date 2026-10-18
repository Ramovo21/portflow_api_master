using System.Reflection;
using Architecture = ArchUnitNET.Domain.Architecture;
using Assembly = System.Reflection.Assembly;
using ArchLoader = ArchUnitNET.Loader.ArchLoader;
using ArchUnitNET.xUnit;
using PortFlow.Shared.Events;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PortFlow.Tests;

/// <summary>= ArchitectureTest (ApplicationModules.verify() + ArchUnit). Stage "architecture" của CI.</summary>
[Trait("Category", "Architecture")]
public class ArchitectureTests
{
    private static readonly string[] Modules = ["Identity", "Customer", "Contract", "Pricing", "Workflow", "ESign"];

    /// <summary>allowedDependencies trong package-info.java của bản Java: module → các module được phép (chỉ qua .Api).</summary>
    private static readonly Dictionary<string, string[]> AllowedDependencies = new()
    {
        ["Identity"] = [],
        ["Customer"] = ["Identity"],
        ["Workflow"] = ["Identity"],
        ["ESign"] = [],
        ["Contract"] = ["Identity", "Customer", "Workflow", "ESign"],
        ["Pricing"] = ["Identity", "Customer", "Contract", "Workflow"],
    };

    private static readonly Assembly[] PortFlowAssemblies = Modules
        .SelectMany(m => new[] { $"PortFlow.{m}", $"PortFlow.{m}.Api" })
        .Append("PortFlow.Shared")
        .Select(Assembly.Load)
        .ToArray();

    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(PortFlowAssemblies).Build();

    [Fact]
    public void ModulesRespectBoundaries()
    {
        var violations = new List<string>();
        foreach (var module in Modules)
        {
            var allowed = AllowedDependencies[module].Select(m => $"PortFlow.{m}.Api")
                .Append("PortFlow.Shared").Append($"PortFlow.{module}.Api").ToHashSet();
            foreach (var assembly in new[] { $"PortFlow.{module}", $"PortFlow.{module}.Api" })
            {
                var referenced = Assembly.Load(assembly).GetReferencedAssemblies()
                    .Select(a => a.Name!)
                    .Where(n => n.StartsWith("PortFlow.", StringComparison.Ordinal));
                violations.AddRange(referenced.Where(r => !allowed.Contains(r)).Select(r => $"{assembly} → {r}"));
            }
            // Phần .Api chỉ được phụ thuộc Shared
            violations.AddRange(Assembly.Load($"PortFlow.{module}.Api").GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("PortFlow.", StringComparison.Ordinal) && n != "PortFlow.Shared")
                .Select(n => $"PortFlow.{module}.Api → {n}"));
        }
        Assert.True(violations.Count == 0, "Module boundary violations:\n" + string.Join('\n', violations.Distinct()));
    }

    [Fact]
    public void DomainDoesNotDependOnWebOrInfrastructure()
    {
        // Thư viện ngoài không nằm trong Architecture → kiểm tra trực tiếp đích của từng dependency.
        Types().That().ResideInNamespaceMatching(@"PortFlow\..*\.Domain(\..*)?")
            .Should().FollowCustomCondition(
                type => !type.Dependencies.Any(d => ForbiddenInDomain.IsMatch(d.Target.FullName)),
                "not depend on web, infrastructure, ASP.NET Core, Npgsql, Dapper or RabbitMQ",
                "depends on a forbidden type")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    private static readonly System.Text.RegularExpressions.Regex ForbiddenInDomain = new(
        @"^(PortFlow\.[^.]+\.(Web|Infrastructure)\.|Microsoft\.AspNetCore\.|Npgsql|Dapper\.|RabbitMQ\.)");

    [Fact]
    public void MoneyIsNeverFloatingPointInDomain()
    {
        var offenders = PortFlowAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace is { } ns && System.Text.RegularExpressions.Regex.IsMatch(ns, @"^PortFlow\..*\.Domain(\..*)?$"))
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => Nullable.GetUnderlyingType(f.FieldType) is { } u ? IsFloating(u) : IsFloating(f.FieldType))
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToList();
        Assert.True(offenders.Count == 0, "double/float fields in domain:\n" + string.Join('\n', offenders));

        static bool IsFloating(Type type) => type == typeof(double) || type == typeof(float);
    }

    [Fact]
    public void WorkflowEngineDoesNotKnowDocumentModules()
    {
        Types().That().ResideInNamespaceMatching(@"PortFlow\.Workflow(\..*)?")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(
                @"PortFlow\.(Contract|Pricing|Billing|Volume|Customer|ESign)(\..*)?"))
            .Because("QĐ-03/NFR-07: the workflow engine must not know specific record types")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    [Fact]
    public void EventsArePublishedOnlyFromApplicationLayer()
    {
        Types().That().ResideInNamespaceMatching(@"PortFlow\..*\.(Domain|Web)(\..*)?")
            .Should().NotDependOnAny(typeof(DomainEventPublisher))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    /// <summary>Mọi endpoint phải khai báo quyền: [RequirePermission]/[Authorize] hoặc [AllowAnonymous] có chủ đích (F01 §7.6).</summary>
    [Fact]
    public void EveryEndpointDeclaresAuthorization()
    {
        var missing = PortFlowAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.IsDefined(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), true))
                .Where(m => !Declares(m) && !Declares(t))
                .Select(m => $"{t.FullName}.{m.Name}"))
            .ToList();
        Assert.True(missing.Count == 0, "Endpoints without authorization:\n" + string.Join('\n', missing));

        static bool Declares(MemberInfo member) =>
            member.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            || member.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true);
    }
}
