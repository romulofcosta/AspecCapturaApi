namespace AspecCapturaApi.Middleware;

/// <summary>
/// Middleware para adicionar headers de segurança em todas as respostas.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;

    public SecurityHeadersMiddleware(RequestDelegate next, ILogger<SecurityHeadersMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        AddSecurityHeaders(context);
        await _next(context);
    }

    private static void AddSecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevenir MIME type sniffing
        headers["X-Content-Type-Options"] = "nosniff";

        // Prevenir clickjacking
        headers["X-Frame-Options"] = "DENY";

        // Política de referrer
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Content Security Policy — API REST pura, sem scripts ou UI
        // Não usar unsafe-inline/unsafe-eval: esta é uma API, não serve HTML
        headers["Content-Security-Policy"] =
            "default-src 'none'; " +
            "frame-ancestors 'none'";

        // Permissions Policy — desabilitar tudo que a API não usa
        headers["Permissions-Policy"] =
            "geolocation=(), " +
            "microphone=(), " +
            "camera=(), " +
            "payment=(), " +
            "usb=()";

        // Remover headers que expõem tecnologia
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.Remove("X-AspNet-Version");
        headers.Remove("X-AspNetMvc-Version");
    }
}

/// <summary>
/// Extension method para facilitar uso do middleware.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
