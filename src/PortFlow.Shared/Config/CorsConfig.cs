using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace PortFlow.Shared.Config;

internal static class CorsConfig
{
    public const string Policy = "portflow-api";

    public static IServiceCollection AddPortFlowCors(this IServiceCollection services, PortFlowOptions options)
    {
        var origins = options.Cors.AllowedOrigins
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return services.AddCors(cors => cors.AddPolicy(Policy, policy => policy
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .AllowAnyHeader()
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromHours(1))));
    }

    /// <summary>Chỉ áp CORS cho <c>/api/**</c> như bản Java.</summary>
    public static IApplicationBuilder UsePortFlowCors(this IApplicationBuilder app) =>
        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), branch => branch.UseCors(Policy));
}
