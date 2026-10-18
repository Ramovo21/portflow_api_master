using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Persistence;

namespace PortFlow.Shared.Web;

/// <summary>Map exception → ProblemDetail (= <c>@RestControllerAdvice GlobalExceptionHandler</c>). Không lộ stacktrace/SQL.</summary>
internal sealed partial class GlobalExceptionHandler(ConstraintErrorRegistry constraints, ILogger<GlobalExceptionHandler> log)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted)
        {
            return false;
        }
        await Problems.WriteAsync(context, ToProblem(exception));
        return true;
    }

    internal ProblemDetails ToProblem(Exception exception)
    {
        switch (exception)
        {
            case DomainException domain:
            {
                var problem = Problems.Of(domain.Code, domain.Message);
                foreach (var (key, value) in domain.Args)
                {
                    problem.Extensions[key] = value;
                }
                return problem;
            }
            case OptimisticLockingFailureException or DbUpdateConcurrencyException:
                return Problems.Of(SysErrorCode.SYS_CONCURRENT_MODIFICATION,
                    "Data has been updated by someone else, please reload");
            case BadHttpRequestException badRequest:
                return Problems.ForStatus(badRequest.StatusCode);
            case ValueProviderException { InnerException: InvalidDataException }:
            case InvalidDataException:
                // Multipart vượt giới hạn kích thước
                return Problems.ForStatus(StatusCodes.Status413PayloadTooLarge);
        }

        if (FindIntegrityViolation(exception) is { } violation)
        {
            return DataIntegrity(exception, violation);
        }

        log.LogError(exception, "Unexpected error");
        return Problems.Of(SysErrorCode.SYS_INTERNAL, "An unexpected error occurred");
    }

    private ProblemDetails DataIntegrity(Exception exception, PostgresException violation)
    {
        var constraint = violation.ConstraintName ?? ConstraintFromMessage(violation.MessageText);
        if (constraints.Lookup(constraint) is { } code)
        {
            return Problems.Of(code, code.Title);
        }
        log.LogError(exception,
            "Unmapped data integrity violation (sqlState={SqlState}, constraint={Constraint}) – register it in ConstraintErrorRegistry",
            violation.SqlState, constraint);
        var problem = Problems.Of(SysErrorCode.SYS_DATA_INTEGRITY, SysErrorCode.SYS_DATA_INTEGRITY.Title);
        if (constraint is not null)
        {
            problem.Extensions["constraint"] = constraint;
        }
        return problem;
    }

    /// <summary>SQLState lớp 23 (integrity constraint violation): 23505 unique, 23P01 exclusion, 23514 check, 23503 FK...</summary>
    private static PostgresException? FindIntegrityViolation(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg && pg.SqlState.StartsWith("23", StringComparison.Ordinal))
            {
                return pg;
            }
        }
        return null;
    }

    private static string? ConstraintFromMessage(string? message)
    {
        if (message is null)
        {
            return null;
        }
        var match = ConstraintName().Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("constraint \"([^\"]+)\"")]
    private static partial Regex ConstraintName();
}
