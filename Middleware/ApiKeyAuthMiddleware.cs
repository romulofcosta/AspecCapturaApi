using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace pwa_camera_poc_api.Middleware
{
    /// <summary>
    /// Middleware for validating API key in X-Api-Key header for v2 endpoints.
    /// All /api/v2/* endpoints require this header to prevent unauthorized access.
    /// API key should be stored in appsettings.json (appsettings.Production.json for prod).
    /// </summary>
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApiKeyAuthMiddleware> _logger;

        private const string API_KEY_HEADER = "X-Api-Key";

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyAuthMiddleware> logger)
        {
            _next = next;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Validates X-Api-Key header for /api/v2/* routes.
        /// Returns 401 Unauthorized if header is missing or invalid.
        /// Returns 500 Internal Server Error if API key is not configured.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            // Only validate /api/v2/* routes
            if (context.Request.Path.StartsWithSegments("/api/v2"))
            {
                // Check if X-Api-Key header exists
                if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var apiKeyValue))
                {
                    _logger.LogWarning($"[ApiKeyAuth] Missing X-Api-Key header for {context.Request.Path}");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Missing X-Api-Key header" });
                    return;
                }

                // Get configured API key from settings
                var configuredApiKey = _configuration["Api:ApiKey"];
                if (string.IsNullOrEmpty(configuredApiKey))
                {
                    _logger.LogError("[ApiKeyAuth] API key not configured in appsettings");
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { error = "API key not configured" });
                    return;
                }

                // Validate API key
                if (!apiKeyValue.ToString().Equals(configuredApiKey, StringComparison.Ordinal))
                {
                    _logger.LogWarning($"[ApiKeyAuth] Invalid X-Api-Key for {context.Request.Path}");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Invalid X-Api-Key" });
                    return;
                }

                _logger.LogDebug($"[ApiKeyAuth] Valid API key for {context.Request.Path}");
            }

            // Continue to next middleware
            await _next(context);
        }
    }
}
