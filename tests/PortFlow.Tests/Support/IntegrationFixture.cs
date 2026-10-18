using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PortFlow.Tests.Support.Fixture;
using PortFlow.Tests.Support.Security;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace PortFlow.Tests.Support;

/// <summary>
/// = AbstractContainersTest/AbstractIntegrationTest: Postgres 17 (pgvector), RabbitMQ 4, MinIO dùng chung cho mọi
/// integration test; schema dựng bằng chính Flyway từ <c>db/</c> (migration + seed + test-migration).
/// JWT do <see cref="TestJwtIssuer"/> ký, nên không cần Keycloak thật.
/// </summary>
public class IntegrationFixture : IAsyncLifetime
{
    private const string FlywayDone = "PORTFLOW_FLYWAY_DONE";

    protected INetwork Network { get; } = new NetworkBuilder().Build();

    public PostgreSqlContainer Postgres { get; }

    public RabbitMqContainer Rabbit { get; }

    public MinioContainer Minio { get; }

    public PortFlowAppFactory App { get; private set; } = null!;

    public IntegrationFixture()
    {
        Postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithNetwork(Network)
            .WithNetworkAliases("postgres")
            .WithDatabase("portflow")
            .WithUsername("portflow")
            .WithPassword("portflow")
            .Build();
        Rabbit = new RabbitMqBuilder("rabbitmq:4-management").Build();
        Minio = new MinioBuilder("cgr.dev/chainguard/minio:latest")
            .WithUsername("portflow")
            .WithPassword("portflow-secret")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Network.CreateAsync();
        await Task.WhenAll(Postgres.StartAsync(), Rabbit.StartAsync(), Minio.StartAsync(), StartExtraAsync());
        await MigrateAsync();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "test");
        App = new PortFlowAppFactory(Settings(), ConfigureServices);
        _ = App.Server; // khởi động app (hosted service: listener, dispatcher)
    }

    public NpgsqlDataSource NewDataSource() => NpgsqlDataSource.Create(Postgres.GetConnectionString());

    protected virtual Task StartExtraAsync() => Task.CompletedTask;

    protected virtual Dictionary<string, string?> Settings() => new()
    {
        ["Portflow:Datasource:Url"] = Postgres.GetConnectionString(),
        ["Portflow:Rabbit:Host"] = Rabbit.Hostname,
        ["Portflow:Rabbit:Port"] = Rabbit.GetMappedPublicPort(5672).ToString(),
        ["Portflow:Rabbit:Username"] = "rabbitmq",
        ["Portflow:Rabbit:Password"] = "rabbitmq",
        ["Portflow:Rabbit:ConfirmTimeout"] = "00:00:03",
        ["Portflow:Rabbit:Listener:AutoStartup"] = "true",
        ["Portflow:Rabbit:Listener:InitialInterval"] = "00:00:00.100",
        ["Portflow:Rabbit:Listener:MaxInterval"] = "00:00:00.500",
        ["Portflow:Storage:Endpoint"] = Minio.GetConnectionString(),
        ["Portflow:Storage:AccessKey"] = "portflow",
        ["Portflow:Storage:SecretKey"] = "portflow-secret",
        ["Portflow:Security:Jwt:Issuer"] = TestJwtIssuer.Issuer,
        ["Portflow:Security:Jwt:JwksUri"] = TestJwtIssuer.JwksUri,
        // Không có Keycloak: đồng bộ thủ công phải báo AUTH_IDP_UNAVAILABLE
        ["Portflow:Keycloak:ServerUrl"] = "http://127.0.0.1:9",
        // Job chạy tay trong test (gọi trực tiếp) để kết quả xác định
        ["Portflow:Jobs:Enabled"] = "false",
    };

    protected virtual void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient("portflow-jwks").ConfigurePrimaryHttpMessageHandler(() => new TestJwtIssuer.JwksHandler());

    private async Task MigrateAsync()
    {
        var db = Path.Combine(AppContext.BaseDirectory, "db");
        var flyway = new ContainerBuilder("flyway/flyway:11")
            .WithNetwork(Network)
            .WithResourceMapping(new DirectoryInfo(db), "/flyway/sql/")
            .WithEntrypoint("sh", "-c")
            .WithCommand("flyway -url=jdbc:postgresql://postgres:5432/portflow -user=portflow -password=portflow "
                         + "-locations=filesystem:/flyway/sql/migration,filesystem:/flyway/sql/seed,filesystem:/flyway/sql/test-migration "
                         + $"migrate && echo {FlywayDone} && sleep 3600")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(FlywayDone))
            .Build();
        await using (flyway)
        {
            await flyway.StartAsync();
        }
    }

    public virtual async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Rabbit.DisposeAsync().AsTask(), Minio.DisposeAsync().AsTask());
        await Network.DisposeAsync();
    }
}

public sealed class PortFlowAppFactory(IReadOnlyDictionary<string, string?> settings, Action<IServiceCollection> configure)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            TestWidgetFixture.Register(services);
            TestSecuredFixture.Register(services);
            PortFlow.Tests.Workflow.Testing.TestApprovableDocuments.Register(services);
            configure(services);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "integration";
}
