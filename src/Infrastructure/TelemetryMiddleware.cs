
namespace MsFundamentals.Trainer.Infrastructure;

public sealed class TelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TelemetryMiddleware> _logger;

    public TelemetryMiddleware(RequestDelegate next, ILogger<TelemetryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        context.Items["AI_tokens_in"] = 0;
        context.Items["AI_tokens_out"] = 0;
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var route = context.Request.Path.ToString();
            var tokensIn = context.Items.TryGetValue("AI_tokens_in", out var tin) ? Convert.ToInt32(tin) : 0;
            var tokensOut = context.Items.TryGetValue("AI_tokens_out", out var tout) ? Convert.ToInt32(tout) : 0;
            var costEst = (tokensIn + tokensOut) / 1_000_000.0 * 1.0; // placeholder $/M tokens; ajuste conforme pricing real
            _logger.LogInformation("[AI_METRICS] route={Route} tokens_in={TokensIn} tokens_out={TokensOut} cost_est=${Cost:F4} duration_ms={Ms}",
                route, tokensIn, tokensOut, costEst, sw.ElapsedMilliseconds);
        }
    }
}
