namespace AspecCapturaApi.Configuration;

/// <summary>
/// Helper para carregar configurações de variáveis de ambiente
/// </summary>
public static class EnvironmentHelper
{
    /// <summary>
    /// Carrega arquivo .env se existir
    /// </summary>
    public static void LoadDotEnv(string? filePath = null)
    {
        filePath ??= Path.Combine(Directory.GetCurrentDirectory(), ".env");

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[INFO] .env file not found at: {filePath}");
            return;
        }

        Console.WriteLine($"[INFO] Loading .env from: {filePath}");

        foreach (var line in File.ReadAllLines(filePath))
        {
            // Ignorar linhas vazias e comentários
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            // Parse linha no formato KEY=VALUE
            var parts = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            // Remover aspas se existirem
            if (value.StartsWith('"') && value.EndsWith('"'))
                value = value[1..^1];
            else if (value.StartsWith('\'') && value.EndsWith('\''))
                value = value[1..^1];

            // Setar variável de ambiente se ainda não existir
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
                Console.WriteLine($"[INFO] Loaded env var: {key}");
            }
        }
    }

    /// <summary>
    /// Obtém variável de ambiente com fallback para configuração
    /// </summary>
    public static string? GetConfigValue(string envKey, IConfiguration config, string configKey)
    {
        return Environment.GetEnvironmentVariable(envKey) ?? config[configKey];
    }

    /// <summary>
    /// Obtém variável de ambiente com valor padrão
    /// </summary>
    public static string GetRequiredEnvVar(string key, string? defaultValue = null)
    {
        var value = Environment.GetEnvironmentVariable(key);
        
        if (string.IsNullOrEmpty(value))
        {
            if (defaultValue != null)
            {
                Console.WriteLine($"[WARN] Environment variable {key} not set, using default");
                return defaultValue;
            }
            
            throw new InvalidOperationException(
                $"Required environment variable '{key}' is not set. " +
                $"Please set it in .env file or system environment.");
        }

        return value;
    }

    /// <summary>
    /// Valida que todas as variáveis obrigatórias estão configuradas
    /// </summary>
    public static void ValidateRequiredEnvVars(params string[] requiredVars)
    {
        var missing = new List<string>();

        foreach (var varName in requiredVars)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(varName)))
                missing.Add(varName);
        }

        if (missing.Any())
        {
            throw new InvalidOperationException(
                $"Missing required environment variables: {string.Join(", ", missing)}. " +
                $"Please configure them in .env file or system environment.");
        }
    }

    /// <summary>
    /// Mascara valor sensível para logging
    /// </summary>
    public static string MaskSensitiveValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "[empty]";
        if (value.Length <= 4) return "****";
        
        return $"{value[..2]}...{value[^2..]}";
    }

    /// <summary>
    /// Log de configuração (sem expor valores sensíveis)
    /// </summary>
    public static void LogConfiguration(ILogger logger)
    {
        logger.LogInformation("=== Configuration ===");
        logger.LogInformation("Environment: {Environment}", 
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
        
        logger.LogInformation("AWS Region: {Region}", 
            Environment.GetEnvironmentVariable("AWS_REGION") ?? "[not set]");
        
        logger.LogInformation("AWS Bucket: {Bucket}", 
            Environment.GetEnvironmentVariable("AWS_BUCKET_NAME") ?? "[not set]");
        
        logger.LogInformation("AWS Access Key: {AccessKey}", 
            MaskSensitiveValue(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")));
        
        logger.LogInformation("JWT Issuer: {Issuer}", 
            Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "[not set]");
        
        logger.LogInformation("JWT Secret: {Secret}", 
            MaskSensitiveValue(Environment.GetEnvironmentVariable("JWT_SECRET")));
        
        logger.LogInformation("=====================");
    }
}
