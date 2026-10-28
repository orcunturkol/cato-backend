using Serilog.Events;

namespace Cato.API.Hosting;

/// <summary>The level of the one request log line (<c>UseSerilogRequestLogging</c>).</summary>
public static class RequestLogLevel
{
    public static LogEventLevel For(HttpContext context, Exception? exception) =>
        // The browser went away (a closed Jobs page aborts its polls): the handler's
        // cancellation is not a server error. On 2026-10-27 five such reads logged as 500 ERR.
        exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested ? LogEventLevel.Information
        : exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/job-agents")
            ? LogEventLevel.Verbose
        : LogEventLevel.Information;
}
