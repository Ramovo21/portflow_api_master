using Microsoft.AspNetCore.Http;

namespace PortFlow.Shared.Web;

/// <summary>Profile worker (<c>Portflow:Api:Enabled=false</c>): chỉ phục vụ <c>/actuator</c>, còn lại 404.</summary>
internal sealed class ApiDisabledMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/actuator"))
        {
            return next(context);
        }
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}
