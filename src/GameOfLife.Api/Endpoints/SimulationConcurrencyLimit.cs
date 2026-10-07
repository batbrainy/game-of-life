using Microsoft.AspNetCore.RateLimiting;

namespace GameOfLife.Api.Endpoints;

public static class SimulationConcurrencyLimit
{
    public const string PolicyName = "simulations";

    public static async ValueTask WriteServerBusyAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        httpContext.Response.Headers.RetryAfter = "1";

        // TryWriteAsync, not WriteAsync: for a request whose Accept header rules out JSON it writes nothing and
        // UseStatusCodePages answers the 503 in plain text, where WriteAsync would throw and turn the 503 into a 500.
        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Server busy",
                Detail = "The server is handling as many simulation requests as it allows. Try again shortly.",
            },
        });
    }
}