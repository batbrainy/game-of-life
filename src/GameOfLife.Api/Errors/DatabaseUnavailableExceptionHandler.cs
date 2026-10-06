using Microsoft.AspNetCore.Diagnostics;

using Npgsql;

namespace GameOfLife.Api.Errors;

/// <summary>
/// Answers a request that failed with a transient database error, such as a refused connection, with 503 problem
/// details. Every other exception keeps the default 500 response.
/// </summary>
public sealed partial class DatabaseUnavailableExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DatabaseUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not NpgsqlException { IsTransient: true })
        {
            return false;
        }

        // The exception handler middleware logs the whole exception at Error before it calls this handler, so this
        // entry adds only what that one lacks instead of logging the exception a second time.
        LogDatabaseUnavailable(logger);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Database unavailable",
                // Fixed text, because the exception's message can name the database host and port, as in
                // "Failed to connect to <host>:<port>".
                Detail = "The database is temporarily unavailable. Try again later.",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A transient database error occurred, so the request was answered with 503 Service Unavailable")]
    private static partial void LogDatabaseUnavailable(ILogger logger);
}