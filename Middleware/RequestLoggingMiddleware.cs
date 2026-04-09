using System.Diagnostics;

namespace AspecCapturaApi.Middleware;

/// <summary>
/// Middleware para logging seguro de requisições
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];

        // Log início da requisição (sem dados sensíveis)
        _logger.LogInformation(
            "[{RequestId}] {Method} {Path} started from {IP}",
            requestId,
            context.Request.Method,
            SanitizePath(context.Request.Path),
            GetClientIp(context));

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{RequestId}] {Method} {Path} failed after {ElapsedMs}ms",
                requestId,
                context.Request.Method,
                SanitizePath(context.Request.Path),
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Log fim da requisição
            var logLevel = context.Response.StatusCode >= 500 
                ? LogLevel.Error 
                : context.Response.StatusCode >= 400 
                    ? LogLevel.Warning 
                    : LogLevel.Information;

            _logger.Log(logLevel,
                "[{RequestId}] {Method} {Path} completed with {StatusCode} in {ElapsedMs}ms",
                requestId,
                context.Request.Method,
                SanitizePath(context.Request.Path),
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Sanitiza path para não logar dados sensíveis
    /// </summary>
    private static string SanitizePath(PathString path)
    {
        var pathStr = path.ToString();

        // Remover query strings que podem conter dados sensíveis
        var queryIndex = pathStr.IndexOf('?');
        if (queryIndex > 0)
            pathStr = pathStr[..queryIndex] + "?[REDACTED]";

        return pathStr;
    }

    /// <summary>
    /// Obtém IP do cliente considerando proxies
    /// </summary>
    private static string GetClientIp(HttpContext context)
    {
        // Tentar obter IP real de trás de proxy
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ips = forwardedFor.Split(',');
            if (ips.Length > 0)
                return ips[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
            return realIp;

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

/// <summary>
/// Extension method para facilitar uso do middleware
/// </summary>
public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestLoggingMiddleware>();
    }
}
