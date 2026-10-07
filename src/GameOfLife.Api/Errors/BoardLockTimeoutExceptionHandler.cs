using GameOfLife.Api.Persistence;

using Microsoft.AspNetCore.Diagnostics;

namespace GameOfLife.Api.Errors;

public sealed class BoardLockTimeoutExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BoardLockTimeoutException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "1";
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Board is busy",
                Detail = "Another operation is updating this board. Try again shortly.",
            },
        });
    }
}