using Microsoft.Extensions.Configuration;

namespace PortFlow.Shared.Config;

/// <summary>
/// Đọc đúng các biến môi trường mà compose/CI của bản Java đang dùng (DB_URL, RABBITMQ_HOST, MINIO_ENDPOINT...)
/// để dùng lại được cấu hình triển khai hiện có.
/// </summary>
public static class LegacyEnvironmentVariables
{
    private static readonly Dictionary<string, string> Mapping = new()
    {
        ["SERVER_PORT"] = "Portflow:Server:Port",
        ["MANAGEMENT_PORT"] = "Portflow:Server:ManagementPort",
        ["DB_URL"] = "Portflow:Datasource:Url",
        ["DB_USERNAME"] = "Portflow:Datasource:Username",
        ["DB_PASSWORD"] = "Portflow:Datasource:Password",
        ["RABBITMQ_HOST"] = "Portflow:Rabbit:Host",
        ["RABBITMQ_PORT"] = "Portflow:Rabbit:Port",
        ["RABBITMQ_USERNAME"] = "Portflow:Rabbit:Username",
        ["RABBITMQ_PASSWORD"] = "Portflow:Rabbit:Password",
        ["MINIO_ENDPOINT"] = "Portflow:Storage:Endpoint",
        ["MINIO_ACCESS_KEY"] = "Portflow:Storage:AccessKey",
        ["MINIO_SECRET_KEY"] = "Portflow:Storage:SecretKey",
        ["MINIO_BUCKET"] = "Portflow:Storage:Bucket",
        ["CORS_ALLOWED_ORIGINS"] = "Portflow:Cors:AllowedOrigins",
        ["KC_ISSUER"] = "Portflow:Security:Jwt:Issuer",
        ["KC_JWKS"] = "Portflow:Security:Jwt:JwksUri",
        ["KC_SERVER_URL"] = "Portflow:Keycloak:ServerUrl",
        ["KC_SYNC_CLIENT_ID"] = "Portflow:Keycloak:ClientId",
        ["KC_SYNC_CLIENT_SECRET"] = "Portflow:Keycloak:ClientSecret",
    };

    public static IConfigurationBuilder AddLegacyEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in Mapping)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
            {
                values[key] = value;
            }
        }
        return builder.AddInMemoryCollection(values);
    }
}
