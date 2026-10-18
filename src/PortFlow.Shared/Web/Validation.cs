using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PortFlow.Shared.Domain;

namespace PortFlow.Shared.Web;

/// <summary>
/// Thay Bean Validation: request body không đọc được → 400 SYS_BAD_REQUEST; vi phạm ràng buộc → 422 SYS_VALIDATION
/// với <c>errors[{field, code, message}]</c>. Ràng buộc khai báo bằng FluentValidation (<c>AbstractValidator&lt;T&gt;</c>).
/// </summary>
internal static class Validation
{
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var unreadable = context.ModelState.Any(e =>
            e.Key.StartsWith('$') || e.Value?.Errors.Any(err => err.Exception is not null) == true);
        var problem = unreadable
            ? Problems.Of(SysErrorCode.SYS_BAD_REQUEST, "Request body is invalid")
            : Problems.Of(SysErrorCode.SYS_VALIDATION, "Submitted data is invalid");
        problem.Extensions["errors"] = context.ModelState
            .Where(e => e.Value is { ValidationState: ModelValidationState.Invalid })
            .SelectMany(e => e.Value!.Errors.Select(err => FieldError(e.Key, "Invalid", err.ErrorMessage)))
            .ToList();
        return Result(context, problem);
    }

    internal static IActionResult Failed(ActionContext context, IEnumerable<FluentValidation.Results.ValidationFailure> failures)
    {
        var problem = Problems.Of(SysErrorCode.SYS_VALIDATION, "Submitted data is invalid");
        problem.Extensions["errors"] = failures
            .Select(f => FieldError(f.PropertyName, CodeOf(f.ErrorCode), f.ErrorMessage))
            .ToList();
        return Result(context, problem);
    }

    private static ObjectResult Result(ActionContext context, ProblemDetails problem)
    {
        Problems.Enrich(context.HttpContext, problem);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { Problems.ContentType },
        };
    }

    private static Dictionary<string, object> FieldError(string field, string code, string message) => new()
    {
        ["field"] = CamelCase(field),
        ["code"] = code,
        ["message"] = message,
    };

    // "NotNullValidator" → "NotNull" (gần tên constraint của Bean Validation)
    private static string CodeOf(string? errorCode) =>
        string.IsNullOrEmpty(errorCode) ? "Invalid" : errorCode.EndsWith("Validator") ? errorCode[..^"Validator".Length] : errorCode;

    private static string CamelCase(string path) =>
        string.Join('.', path.TrimStart('$', '.').Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}

/// <summary>Tự chạy mọi <c>IValidator&lt;T&gt;</c> đã đăng ký cho tham số action, sau model binding và trước idempotency.</summary>
internal sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => -1500;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }
            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                context.Result = Validation.Failed(context, result.Errors);
                return;
            }
        }
        await next();
    }
}
