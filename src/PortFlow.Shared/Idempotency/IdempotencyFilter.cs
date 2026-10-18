using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Npgsql;
using PortFlow.Shared.Config;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;

namespace PortFlow.Shared.Idempotency;

/// <summary>
/// Bọc action có <see cref="IdempotentAttribute"/> trong một transaction (= IdempotencyAspect + TransactionTemplate):
/// INSERT key → chạy use case (cùng transaction) → lưu response → COMMIT. Use case lỗi → rollback cả key.
/// </summary>
internal sealed class IdempotencyFilter(
    IdempotencyRepository repository,
    ITransactionRunner tx,
    ICurrentUserProvider users,
    PortFlowMetrics metrics) : IAsyncActionFilter, IOrderedFilter
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private const string LockNotAvailable = "55P03";

    public int Order => 0;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || !action.MethodInfo.IsDefined(typeof(IdempotentAttribute), inherit: true))
        {
            await next();
            return;
        }
        RequireActionResult(action);

        var request = context.HttpContext.Request;
        var key = RequireKey(request.Headers[IdempotentAttribute.Header].ToString());
        var userId = users.Current().Id;
        var method = request.Method;
        var path = (request.PathBase + request.Path).ToString();
        var hash = RequestHash(method, path, action, context.ActionArguments);
        var ct = context.HttpContext.RequestAborted;

        try
        {
            await tx.ExecuteAsync(async _ =>
            {
                await repository.SetLockTimeoutAsync(LockTimeout, ct);
                if (!await InsertAsync(key, userId, method, path, hash, ct))
                {
                    context.Result = await ReplayOrRejectAsync(key, userId, hash, ct);
                    return;
                }
                var executed = await next();
                if (executed.Exception is not null && !executed.ExceptionHandled)
                {
                    // Để MVC ném lại exception gốc sau khi đã rollback.
                    throw new RollbackSignal();
                }
                var (status, body) = Capture(executed.Result);
                await repository.CompleteAsync(key, userId, status, body, ct);
            }, ct);
        }
        catch (RollbackSignal)
        {
        }
    }

    private async Task<bool> InsertAsync(string key, Guid userId, string method, string path, string hash,
        CancellationToken ct)
    {
        try
        {
            return await repository.TryInsertAsync(key, userId, method, path, hash, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == LockNotAvailable)
        {
            throw new DomainException(SysErrorCode.SYS_IDEMPOTENCY_IN_PROGRESS);
        }
    }

    private async Task<IActionResult> ReplayOrRejectAsync(string key, Guid userId, string hash, CancellationToken ct)
    {
        var stored = await repository.FindAsync(key, userId, ct)
                     ?? throw new DomainException(SysErrorCode.SYS_IDEMPOTENCY_IN_PROGRESS);
        if (stored.RequestHash != hash)
        {
            throw new DomainException(SysErrorCode.SYS_IDEMPOTENCY_KEY_REUSED);
        }
        if (!stored.Completed)
        {
            throw new DomainException(SysErrorCode.SYS_IDEMPOTENCY_IN_PROGRESS);
        }
        metrics.IdempotencyReplays.Add(1);
        return new ReplayResult(stored.ResponseStatus!.Value, stored.ResponseBody);
    }

    private static (int Status, string? Body) Capture(IActionResult? result) => result switch
    {
        ObjectResult o => (o.StatusCode ?? StatusCodes.Status200OK,
            o.Value is null ? null : JsonSerializer.Serialize(o.Value, o.Value.GetType(), JsonConfig.Options)),
        IStatusCodeActionResult s => (s.StatusCode ?? StatusCodes.Status200OK, null),
        EmptyResult or null => (StatusCodes.Status200OK, null),
        _ => throw new InvalidOperationException(
            $"@Idempotent action returned an unsupported result type: {result.GetType().Name}"),
    };

    private static void RequireActionResult(ControllerActionDescriptor action)
    {
        var type = action.MethodInfo.ReturnType;
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>)
                                   || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            type = type.GetGenericArguments()[0];
        }
        var ok = typeof(IActionResult).IsAssignableFrom(type)
                 || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>));
        if (!ok)
        {
            throw new InvalidOperationException(
                $"@Idempotent action must return IActionResult or ActionResult<T>: {action.DisplayName}");
        }
    }

    private static string RequireKey(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            throw new DomainException(SysErrorCode.SYS_IDEMPOTENCY_KEY_REQUIRED);
        }
        if (!Guid.TryParse(header.Trim(), out var key))
        {
            throw new DomainException(SysErrorCode.SYS_IDEMPOTENCY_KEY_REQUIRED, "Idempotency-Key must be a UUID");
        }
        return key.ToString();
    }

    /// <summary>SHA-256(method | path | JSON các tham số action theo thứ tự khai báo), tệp upload mô tả bằng tên + kích thước.</summary>
    private static string RequestHash(string method, string path, ControllerActionDescriptor action,
        IDictionary<string, object?> arguments)
    {
        var material = new Dictionary<string, object?>();
        foreach (var parameter in action.Parameters)
        {
            if (!arguments.TryGetValue(parameter.Name, out var arg) || arg is null
                || arg is HttpContext or HttpRequest or HttpResponse or ClaimsPrincipal or CancellationToken)
            {
                continue;
            }
            material[parameter.Name] = Describe(arg);
        }
        var canonical = method + "|" + path + "|" + JsonSerializer.Serialize(material, JsonConfig.Options);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static object Describe(object arg) => arg switch
    {
        IFormFile file => new Dictionary<string, object?>
        {
            ["name"] = file.Name,
            ["originalFilename"] = file.FileName,
            ["size"] = file.Length,
            ["contentType"] = file.ContentType,
        },
        IEnumerable<IFormFile> files => files.Select(Describe).ToList(),
        _ => arg,
    };

    private sealed class RollbackSignal : Exception;

    /// <summary>Phát lại response đã lưu, kèm <c>Idempotent-Replayed: true</c>.</summary>
    private sealed class ReplayResult(int status, string? body) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = status;
            response.Headers[IdempotentAttribute.ReplayedHeader] = "true";
            if (body is null or "null")
            {
                return;
            }
            response.ContentType = "application/json; charset=utf-8";
            await response.WriteAsync(body, Encoding.UTF8);
        }
    }
}
