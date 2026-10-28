using Cato.API.Hosting;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Cato.API.Tests.Hosting;

public class RequestLogLevelTests
{
    private static DefaultHttpContext Context(string path, int status = 200, bool aborted = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        context.Response.StatusCode = status;
        if (aborted)
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            context.RequestAborted = cts.Token;
        }
        return context;
    }

    [Fact]
    public void A_request_the_client_aborted_is_not_an_error()
    {
        var level = RequestLogLevel.For(Context("/api/job-runs", 500, aborted: true), new TaskCanceledException());
        Assert.Equal(LogEventLevel.Information, level);
    }

    [Fact]
    public void A_cancellation_the_client_did_not_cause_and_any_500_stay_errors()
    {
        Assert.Equal(LogEventLevel.Error, RequestLogLevel.For(Context("/api/job-runs", 500), new TaskCanceledException()));
        Assert.Equal(LogEventLevel.Error, RequestLogLevel.For(Context("/api/job-runs", 500), null));
        Assert.Equal(LogEventLevel.Error, RequestLogLevel.For(Context("/api/job-runs", 500, aborted: true), new InvalidOperationException()));
    }

    [Fact]
    public void Health_probes_stay_verbose_and_reads_information()
    {
        Assert.Equal(LogEventLevel.Verbose, RequestLogLevel.For(Context("/health/ready"), null));
        Assert.Equal(LogEventLevel.Information, RequestLogLevel.For(Context("/api/games"), null));
    }
}
