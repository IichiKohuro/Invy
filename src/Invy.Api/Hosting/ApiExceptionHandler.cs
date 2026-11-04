using Invy.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Invy.Api.Hosting;

/// <summary>
/// Ошибки сценариев → ProblemDetails (RFC 9457): 404 — не найдено, 409 — конфликт версий (в <c>current</c> —
/// актуальное состояние), 422 — ошибки ввода по полям, 400 — некорректный запрос.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            NotFoundException e => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Не найдено", Detail = e.Message },
            ConflictException e => Conflict(e),
            ValidationException e => new ValidationProblemDetails(e.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = e.Message,
            },
            BadHttpRequestException e => new ProblemDetails { Status = e.StatusCode, Title = "Некорректный запрос", Detail = e.Message },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails Conflict(ConflictException e)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Конфликт", Detail = e.Message };
        if (e.Current is not null)
        {
            problem.Extensions["current"] = e.Current;
        }

        return problem;
    }
}
