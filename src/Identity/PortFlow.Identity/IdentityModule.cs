using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PortFlow.Identity.Api;
using PortFlow.Identity.Application;
using PortFlow.Identity.Infrastructure.Keycloak;
using PortFlow.Identity.Infrastructure.Persistence;
using PortFlow.Identity.Infrastructure.Security;
using PortFlow.Shared;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Scheduling;
using PortFlow.Shared.Security;

namespace PortFlow.Identity;

/// <summary>F01 – Identity &amp; AuthZ: Keycloak lo đăng nhập (AuthN), PortFlow tự phân quyền 3 tầng (AuthZ).</summary>
public static class IdentityModule
{
    public static WebApplicationBuilder AddPortFlowIdentity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
        services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.Section));

        services.AddPortFlowDbContext<IdentityDbContext>();
        services.AddSingleton<IConstraintErrorMapping, IdentityConstraintErrorMapping>();
        services.AddPortFlowValidators(typeof(IdentityModule));

        // Truy vấn công khai cho module khác (identity.api)
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IdentityQueries>();
        services.AddScoped<DocumentTypeService>();
        services.AddScoped<IDocumentTypeQuery>(sp => sp.GetRequiredService<DocumentTypeService>());
        services.AddScoped<DelegationService>();
        services.AddScoped<IDelegationQuery>(sp => sp.GetRequiredService<DelegationService>());
        services.AddSingleton<IDataScopeQuery, RoleDataScopeQuery>();

        // Bản sao user + người dùng hiện tại
        services.AddSingleton<IRolePermissions, RolePermissionCache>();
        services.AddSingleton<CurrentUserCache>();
        services.AddScoped<UserSyncService>();
        services.AddScoped<IUserMirrorSync>(sp => sp.GetRequiredService<UserSyncService>());
        services.AddScoped<CurrentUserResolver>();
        services.AddScoped<DepartmentService>();
        services.AddScoped<RoleAdminService>();
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<ICurrentUserProvider, HttpContextCurrentUserProvider>());

        // Keycloak Admin API + job đồng bộ
        services.AddHttpClient<KeycloakAdminClient>(KeycloakAdminClient.HttpClientName,
            http => http.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<KeycloakSyncService>();
        services.AddScheduledJob<KeycloakUserSyncJob>();

        // AuthN: JWT Bearer (Keycloak) – AuthZ: permission (tầng 1) + scope (tầng 2)
        services.AddHttpClient(JwtAuthentication.JwksHttpClient);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
            ProblemAuthorizationResultHandler>();
        services.AddScoped<ScopedPermissionEvaluator>();
        services.AddScoped<IScopePermissionEvaluator>(sp => sp.GetRequiredService<ScopedPermissionEvaluator>());
        services.AddScoped<ScopeAuthorizationFilter>();
        services.Configure<MvcOptions>(mvc => mvc.Filters.AddService<ScopeAuthorizationFilter>());
        return builder;
    }
}
