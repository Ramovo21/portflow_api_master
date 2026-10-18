using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using PortFlow.Shared.Config;
using PortFlow.Shared.Domain;

namespace PortFlow.Shared.Web;

/// <summary>Dựng ProblemDetail (RFC 9457) theo định dạng lỗi chuẩn F00 §5.1.</summary>
public static class Problems
{
    public const string ErrorTypeBase = "https://portflow.local/errors/";
    public const string ContentType = "application/problem+json";

    public static ProblemDetails Of(IErrorCode code, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = ErrorTypeBase + code.Code,
            Title = code.Title,
            Status = (int)code.Status,
            Detail = detail,
        };
        problem.Extensions["errorCode"] = code.Code;
        return problem;
    }

    public static ErrorCode FallbackCode(int status) => status switch
    {
        400 => SysErrorCode.SYS_BAD_REQUEST,
        401 => SysErrorCode.SYS_UNAUTHORIZED,
        403 => SysErrorCode.SYS_FORBIDDEN,
        404 => SysErrorCode.SYS_NOT_FOUND,
        405 => SysErrorCode.SYS_METHOD_NOT_ALLOWED,
        413 => SysErrorCode.SYS_FILE_TOO_LARGE,
        415 => SysErrorCode.SYS_UNSUPPORTED_MEDIA_TYPE,
        422 => SysErrorCode.SYS_VALIDATION,
        >= 400 and < 500 => SysErrorCode.SYS_BAD_REQUEST,
        _ => SysErrorCode.SYS_INTERNAL,
    };

    /// <summary>Lỗi do framework sinh (404 route, 405, 415...) cũng mang errorCode.</summary>
    public static ProblemDetails ForStatus(int status)
    {
        var code = FallbackCode(status);
        var problem = Of(code, code.Title);
        problem.Status = status;
        problem.Title = ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase ? phrase : code.Title;
        return problem;
    }

    public static Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        Enrich(context, problem);
        context.Response.StatusCode = problem.Status ?? (int)HttpStatusCode.InternalServerError;
        return context.Response.WriteAsJsonAsync(problem, JsonConfig.Options, ContentType);
    }

    public static void Enrich(HttpContext context, ProblemDetails problem)
    {
        problem.Instance ??= context.Request.PathBase + context.Request.Path;
        problem.Extensions["correlationId"] = CorrelationId.Current;
        if (CorrelationId.Current is { } cid)
        {
            // UseExceptionHandler xóa header của response trước khi gọi handler → gắn lại.
            context.Response.Headers[CorrelationId.Header] = cid;
        }
        if (!problem.Extensions.ContainsKey("errors"))
        {
            problem.Extensions["errors"] = Array.Empty<object>();
        }
    }
}
