using Microsoft.AspNetCore.Diagnostics;

using Npgsql;

namespace GameOfLife.Api.Errors;

/// <summary>
/// Answers a request that failed with a transient database error, such as a refused connection, with 503 problem
/// details. Every other exception keeps the default 500 response.
/// </summary>
public sealed class DatabaseUnavailableExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not NpgsqlException { IsTransient: true })
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Database unavailable",
                // Exception messages can expose database connection details.
                Detail = "The database is temporarily unavailable. Try again later.",
            },
        });
    }
}