using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ValidationSystem.Core.Services;
using ValidationSystem.Core.Validation;

namespace ValidationSystem.Api;

/// <summary>
/// Maps exceptions to RFC 7807 problem responses with Hebrew, business-level messages.
/// Technical details are logged, never returned to the client.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, extensions) = exception switch
        {
            InvalidDefinitionException ide => (StatusCodes.Status400BadRequest, "הגדרת הבדיקה אינה תקינה",
                new Dictionary<string, object?> { ["errors"] = ide.Errors }),
            NotFoundException nf => (StatusCodes.Status404NotFound, nf.Message, null),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "הבקשה אינה תקינה", null),
            OperationCanceledException => (499, "הבקשה בוטלה", null),
            _ => (StatusCodes.Status500InternalServerError, "אירעה שגיאה בלתי צפויה. נסו שוב מאוחר יותר.", null)
        };

        if (status >= 500) logger.LogError(exception, "Unhandled error for {Path}", context.Request.Path);

        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title };
        if (extensions is not null)
            foreach (var (key, value) in extensions) problem.Extensions[key] = value;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
