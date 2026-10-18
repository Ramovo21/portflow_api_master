using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using PortFlow.Shared.Attachment;
using PortFlow.Shared.Config;
using PortFlow.Shared.Messaging;
using Prometheus;

namespace PortFlow.Shared.Web;

/// <summary>
/// Endpoint vận hành giống Spring Boot Actuator: <c>/actuator/health</c> (db, rabbit, minio),
/// <c>/actuator/health/liveness|readiness</c>, <c>/actuator/info</c>, <c>/actuator/prometheus</c>.
/// </summary>
internal static class Actuator
{
    public static IServiceCollection AddPortFlowHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DbHealthCheck>("db")
            .AddCheck<RabbitHealthCheck>("rabbit")
            .AddCheck<MinioHealthCheck>("minio");
        return services;
    }

    public static IEndpointRouteBuilder MapActuator(this IEndpointRouteBuilder endpoints)
    {
        // Chỉ health là public; info/prometheus cần đăng nhập (giống SecurityConfig bản Java).
        endpoints.MapHealthChecks("/actuator/health", Options(_ => true)).AllowAnonymous();
        endpoints.MapHealthChecks("/actuator/health/liveness", Options(_ => false)).AllowAnonymous();
        endpoints.MapHealthChecks("/actuator/health/readiness", Options(_ => false)).AllowAnonymous();
        endpoints.MapGet("/actuator/info", () => Results.Json(new Dictionary<string, object>()));
        endpoints.MapMetrics("/actuator/prometheus");
        return endpoints;
    }

    private static HealthCheckOptions Options(Func<HealthCheckRegistration, bool> predicate) => new()
    {
        Predicate = predicate,
        ResponseWriter = WriteAsync,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        },
    };

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var body = new Dictionary<string, object> { ["status"] = Status(report.Status) };
        if (report.Entries.Count > 0)
        {
            body["components"] = report.Entries.ToDictionary(e => e.Key,
                e => (object)new Dictionary<string, object> { ["status"] = Status(e.Value.Status) });
        }
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonConfig.Options));
    }

    private static string Status(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "UP",
        HealthStatus.Degraded => "DEGRADED",
        _ => "DOWN",
    };

    private sealed class DbHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            try
            {
                await using var command = dataSource.CreateCommand("SELECT 1");
                await command.ExecuteScalarAsync(ct);
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(exception: ex);
            }
        }
    }

    private sealed class RabbitHealthCheck(RabbitConnectionProvider connections) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var connection = await connections.GetConnectionAsync(timeout.Token);
                return connection.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(exception: ex);
            }
        }
    }

    private sealed class MinioHealthCheck(IObjectStorage storage) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
            await storage.IsAvailableAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }
}
