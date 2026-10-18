using System.Diagnostics.Metrics;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using PortFlow.Shared.Attachment;
using PortFlow.Shared.Config;
using PortFlow.Shared.Events;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Messaging;
using PortFlow.Shared.Outbox;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Scheduling;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;
using PortFlow.Shared.Web;

namespace PortFlow.Shared;

/// <summary>Đăng ký toàn bộ hạ tầng F00 (shared kernel) vào host.</summary>
public static class SharedModule
{
    /// <summary>Giới hạn multipart: tệp ≤ 25 MB do service kiểm tra, request ≤ 26 MB do Kestrel chặn.</summary>
    private const long MaxRequestSize = 26L * 1024 * 1024;

    public static WebApplicationBuilder AddPortFlowShared(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var section = builder.Configuration.GetSection(PortFlowOptions.Section);
        services.Configure<PortFlowOptions>(section);
        var options = section.Get<PortFlowOptions>() ?? new PortFlowOptions();

        DapperSetup.Register();
        services.AddSingleton(sp => DataSourceFactory.Create(sp.GetRequiredService<IOptions<PortFlowOptions>>().Value.Datasource));
        services.AddScoped<DbSession>();
        services.AddScoped<ITransactionRunner, TransactionRunner>();
        services.AddScoped<DbSessionInterceptor>();
        services.AddSingleton(sp => new ConstraintErrorRegistry(sp.GetServices<IConstraintErrorMapping>()));

        services.AddSingleton<IBusinessClock, SystemBusinessClock>();
        services.TryAddSingleton<ICurrentUserProvider, SystemCurrentUserProvider>();
        services.AddSingleton<EventMetadataFactory>();
        services.AddSingleton<PortFlowMetrics>();

        // Outbox (Event Publication Registry) + RabbitMQ
        services.AddSingleton<RabbitConnectionProvider>();
        services.AddSingleton<RabbitEventExternalizer>();
        services.AddSingleton(sp => new EventTargets(sp.GetServices<DomainEventListenerRegistration>()));
        services.AddSingleton(sp => new EventPublicationRepository(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddSingleton<EventPublicationDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<EventPublicationDispatcher>());
        services.AddSingleton<IncompleteEventPublications>();
        services.AddSingleton(sp => new OutboxAdministration(
            sp.GetRequiredService<EventPublicationRepository>(), sp.GetRequiredService<IncompleteEventPublications>()));
        services.AddScoped(sp => new DomainEventPublisher(sp.GetRequiredService<DbSession>(),
            sp.GetRequiredService<ITransactionRunner>(), sp.GetRequiredService<EventTargets>(),
            sp.GetRequiredService<EventPublicationDispatcher>()));
        services.AddScoped<JdbcInboxGuard>();
        services.AddScoped<IInboxGuard>(sp => sp.GetRequiredService<JdbcInboxGuard>());
        services.AddHostedService<RabbitListenerHostedService>();
        services.AddHostedService<OutboxIncompleteGauge>();

        // Job định kỳ (= @Scheduled + ShedLock)
        services.AddSingleton<JobLockProvider>();
        services.AddHostedService<JobSchedulerHostedService>();
        services.AddScoped<IdempotencyRepository>();
        services.AddScheduledJob<IdempotencyCleanupJob>();
        services.AddScheduledJob<InboxCleanupJob>();
        services.AddScheduledJob<OutboxResubmitJob>();
        services.AddScheduledJob<OutboxCleanupJob>();

        services.AddPortFlowStorage();

        // Web
        services.AddPortFlowCors(options);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddPortFlowHealthChecks();
        services.AddAuthorization();
        services.AddScoped<IdempotencyFilter>();
        services.AddScoped<FluentValidationFilter>();
        services.AddControllers(mvc =>
            {
                mvc.Filters.AddService<FluentValidationFilter>();
                mvc.Filters.AddService<IdempotencyFilter>();
                mvc.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddApplicationPart(typeof(SharedModule).Assembly)
            .AddJsonOptions(json => JsonConfig.Configure(json.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(api => api.InvalidModelStateResponseFactory = Validation.InvalidModelState);
        services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = MaxRequestSize);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestSize);

        // Profile worker chỉ mở cổng quản trị (actuator); api mở cổng chính.
        var port = options.Api.Enabled ? options.Server.Port : options.Server.ManagementPort;
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        return builder;
    }

    /// <summary>Đăng ký validator FluentValidation của một assembly module.</summary>
    public static IServiceCollection AddPortFlowValidators(this IServiceCollection services, Type assemblyMarker) =>
        services.AddValidatorsFromAssemblyContaining(assemblyMarker, ServiceLifetime.Singleton);

    public static WebApplication UsePortFlowShared(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<PortFlowOptions>>().Value;
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages(async context =>
        {
            var response = context.HttpContext.Response;
            if (response.HasStarted || response.ContentLength > 0 || !string.IsNullOrEmpty(response.ContentType))
            {
                return;
            }
            await Problems.WriteAsync(context.HttpContext, Problems.ForStatus(response.StatusCode));
        });
        if (!options.Api.Enabled)
        {
            app.UseMiddleware<ApiDisabledMiddleware>();
        }
        app.UsePortFlowCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapActuator();
        app.MapControllers();
        return app;
    }
}

/// <summary>Gauge <c>outbox.incomplete.count</c> (số publication chưa hoàn thành), đo khi scrape.</summary>
internal sealed class OutboxIncompleteGauge(PortFlowMetrics metrics, EventPublicationRepository repository) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Meter.CreateObservableGauge("outbox.incomplete.count", Observe,
            description: "Number of incomplete event publications");
        return Task.CompletedTask;
    }

    private IEnumerable<Measurement<long>> Observe()
    {
        try
        {
            return [new Measurement<long>(repository.CountIncomplete())];
        }
        catch (Exception)
        {
            // DB không truy cập được: bỏ qua lần đo này
            return [];
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
