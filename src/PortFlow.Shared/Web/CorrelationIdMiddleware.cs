using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PortFlow.Shared.Web;

/// <summary>Chạy đầu tiên: lấy/sinh <c>X-Correlation-Id</c>, gắn vào response và log scope.</summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var cid = CorrelationId.Sanitize(context.Request.Headers[CorrelationId.Header].ToString() is { Length: > 0 } h ? h : null);
        var previous = CorrelationId.Set(cid);
        context.Response.Headers[CorrelationId.Header] = cid;
        using var scope = logger.BeginScope(new Dictionary<string, object> { [CorrelationId.LogKey] = cid });
        try
        {
            await next(context);
        }
        finally
        {
            CorrelationId.Set(previous);
        }
    }
}
