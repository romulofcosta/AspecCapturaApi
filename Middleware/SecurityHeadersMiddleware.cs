namespace AspecCapturaApi.Middleware;

/// <summary>
/// Middleware para adicionar headers de segurança em todas as respostas
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
        // Adicionar headers de segurança
        AddSecurityHeaders(context);

        await _next(context);
    }

    private void AddSecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevenir MIME type sniffing
        if (!headers.ContainsKey("X-Content-Type-Options"))
            headers.Add("X-Content-Type-Options", "nosniff");

        // Prevenir clickjacking
        if (!headers.ContainsKey("X-Frame-Options"))
            headers.Add("X-Frame-Options", "DENY");

        // Habilitar proteção XSS do browser
        if (!headers.ContainsKey("X-XSS-Protection"))
            headers.Add("X-XSS-Protection", "1; mode=block");

        // Política de referrer
        if (!headers.ContainsKey("Referrer-Policy"))
            headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

        // Content Security Policy (básico para MVP)
        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            var csp = "default-src 'self'; " +
                      "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " + // unsafe-* para Blazor
                      "style-src 'self' 'unsafe-inline'; " +
                      "img-src 'self' data: https:; " +
                      "font-src 'self' data:; " +
                      "connect-src 'self' https://*.amazonaws.com; " +
                      "frame-ancestors 'none'";
            
            headers.Add("Content-Security-Policy", csp);
        }

        // Permissions Policy (desabilitar features não usadas)
        if (!headers.ContainsKey("Permissions-Policy"))
        {
            headers.Add("Permissions-Policy", 
                "geolocation=(), " +
                "microphone=(), " +
                "camera=(self), " + // Permitir camera para captura de fotos
                "payment=(), " +
                "usb=()");
        }

        // Remover header que expõe tecnologia
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.Remove("X-AspNet-Version");
        headers.Remove("X-AspNetMvc-Version");
    }
}

/// <summary>
/// Extension method para facilitar uso do middleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
