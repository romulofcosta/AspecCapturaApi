using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AspecCapturaApi.Services;

public interface IAuthService
{
    string GenerateJwtToken(string userId, string username, string prefix, string esfera);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
    bool ValidatePassword(string password, string hashedPassword);
    string HashPassword(string password);
}

public class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IConfiguration configuration, ILogger<AuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string GenerateJwtToken(string userId, string username, string prefix, string esfera)
    {
        // Obter secret do ambiente ou configuração
        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") 
            ?? _configuration["Security:JwtSecret"]
            ?? throw new InvalidOperationException("JWT_SECRET not configured");

        if (jwtSecret.Length < 32)
        {
            _logger.LogWarning("JWT_SECRET is too short. Use at least 256 bits (32 characters)");
        }

        var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") 
            ?? _configuration["Security:JwtIssuer"] 
            ?? "aspec-capture-api";
        
        var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") 
            ?? _configuration["Security:JwtAudience"] 
            ?? "aspec-capture-client";
        
        var expirationMinutes = int.TryParse(
            Environment.GetEnvironmentVariable("JWT_EXPIRATION_MINUTES"), 
            out var envMinutes) 
                ? envMinutes 
                : _configuration.GetValue<int>("Security:JwtExpirationMinutes", 60);

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim("prefix", prefix),
            new Claim("esfera", esfera),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") 
            ?? _configuration["Security:JwtSecret"];
        
        if (string.IsNullOrEmpty(jwtSecret)) 
        {
            _logger.LogError("JWT_SECRET not configured");
            return null;
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(jwtSecret);

        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") 
                    ?? _configuration["Security:JwtIssuer"],
                ValidateAudience = true,
                ValidAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") 
                    ?? _configuration["Security:JwtAudience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero // Sem tolerância de tempo
            }, out SecurityToken validatedToken);

            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Token expired");
            return null;
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("Invalid token signature");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    /// <summary>
    /// Valida senha usando comparação simples (para MVP)
    /// TODO: Migrar para BCrypt antes de produção
    /// </summary>
    public bool ValidatePassword(string password, string storedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedPassword))
            return false;

        // Para MVP: comparação simples
        // TODO: Implementar BCrypt.Verify antes de produção
        return string.Equals(password.Trim(), storedPassword.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Hash de senha simples (para MVP)
    /// TODO: Migrar para BCrypt antes de produção
    /// </summary>
    public string HashPassword(string password)
    {
        // Para MVP: retorna senha sem hash
        // TODO: Implementar BCrypt.HashPassword antes de produção
        _logger.LogWarning("Using plain text password storage. Implement BCrypt before production!");
        return password;
    }
}
