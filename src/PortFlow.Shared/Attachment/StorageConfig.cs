using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using PortFlow.Shared.Config;

namespace PortFlow.Shared.Attachment;

internal static class StorageConfig
{
    public static IServiceCollection AddPortFlowStorage(this IServiceCollection services)
    {
        services.AddSingleton<IObjectStorage>(sp =>
        {
            var storage = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PortFlowOptions>>().Value.Storage;
            var endpoint = new Uri(storage.Endpoint);
            var client = new MinioClient()
                .WithEndpoint(endpoint.Host, endpoint.Port)
                .WithCredentials(storage.AccessKey, storage.SecretKey)
                .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps)
                .Build();
            return new MinioObjectStorage(client, storage.Bucket, sp.GetRequiredService<ILogger<MinioObjectStorage>>());
        });
        services.AddScoped(sp => new DefaultAttachmentService(
            sp.GetRequiredService<Persistence.DbSession>(),
            sp.GetRequiredService<Persistence.ITransactionRunner>(),
            sp.GetRequiredService<IObjectStorage>(),
            sp.GetRequiredService<Security.ICurrentUserProvider>(),
            sp.GetRequiredService<Time.IBusinessClock>(),
            sp.GetServices<IAttachmentAccessPolicy>()));
        services.AddScoped<IAttachmentService>(sp => sp.GetRequiredService<DefaultAttachmentService>());
        return services;
    }
}
