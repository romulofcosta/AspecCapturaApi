using System.Text.RegularExpressions;

namespace AspecCapturaApi.Validators;

/// <summary>
/// Validador de entrada para prevenir injeções e garantir formato correto dos dados
/// </summary>
public static class InputValidator
{
    // Regex compilados para melhor performance
    private static readonly Regex PrefixRegex = new(@"^[A-Z]{2}\d{3}$", RegexOptions.Compiled);
    private static readonly Regex NutombRegex = new(@"^[A-Z0-9]{1,20}$", RegexOptions.Compiled);
    private static readonly Regex IdPatombRegex = new(@"^[A-Z0-9\-]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex EstadoRegex = new(@"^[A-ZÁÉÍÓÚÂÊÔÃÕÇ\s]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex AlphanumericRegex = new(@"^[A-Za-z0-9\s\-_]{1,100}$", RegexOptions.Compiled);

    /// <summary>
    /// Valida formato de prefixo de município (ex: CE999, SP001)
    /// </summary>
    public static bool IsValidPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return false;
        return PrefixRegex.IsMatch(prefix);
    }

    /// <summary>
    /// Valida formato de número de tombamento
    /// </summary>
    public static bool IsValidNutomb(string? nutomb)
    {
        if (string.IsNullOrWhiteSpace(nutomb)) return false;
        return NutombRegex.IsMatch(nutomb);
    }

    /// <summary>
    /// Valida formato de ID de patrimônio
    /// </summary>
    public static bool IsValidIdPatomb(string? idPatomb)
    {
        if (string.IsNullOrWhiteSpace(idPatomb)) return false;
        return IdPatombRegex.IsMatch(idPatomb);
    }

    /// <summary>
    /// Valida formato de estado (ex: BOM, REGULAR, RUIM)
    /// </summary>
    public static bool IsValidEstado(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado)) return true; // Opcional
        return EstadoRegex.IsMatch(estado);
    }

    /// <summary>
    /// Valida formato alfanumérico genérico
    /// </summary>
    public static bool IsValidAlphanumeric(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return true; // Opcional
        return AlphanumericRegex.IsMatch(input);
    }

    /// <summary>
    /// Sanitiza string removendo caracteres perigosos
    /// </summary>
    public static string SanitizeString(string? input, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Remove caracteres perigosos para XSS e injeção
        var sanitized = input
            .Replace("<", "")
            .Replace(">", "")
            .Replace("\"", "")
            .Replace("'", "")
            .Replace("&", "")
            .Replace(";", "")
            .Replace("--", "")
            .Replace("/*", "")
            .Replace("*/", "")
            .Trim();

        // Limita tamanho
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength];

        return sanitized;
    }

    /// <summary>
    /// Valida e sanitiza prefixo
    /// </summary>
    public static (bool IsValid, string? Error, string? Sanitized) ValidateAndSanitizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return (false, "Prefixo é obrigatório", null);

        var sanitized = prefix.Trim().ToUpperInvariant();

        if (!IsValidPrefix(sanitized))
            return (false, "Formato de prefixo inválido. Use formato: XX999 (ex: CE999)", null);

        return (true, null, sanitized);
    }

    /// <summary>
    /// Valida e sanitiza nutomb
    /// </summary>
    public static (bool IsValid, string? Error, string? Sanitized) ValidateAndSanitizeNutomb(string? nutomb)
    {
        if (string.IsNullOrWhiteSpace(nutomb))
            return (false, "Número de tombamento é obrigatório", null);

        var sanitized = nutomb.Trim().ToUpperInvariant();

        if (!IsValidNutomb(sanitized))
            return (false, "Formato de nutomb inválido. Use apenas letras e números (máx 20 caracteres)", null);

        return (true, null, sanitized);
    }

    /// <summary>
    /// Valida tamanho de batch para operações em lote
    /// </summary>
    public static bool IsValidBatchSize(int size, int maxSize = 50)
    {
        return size > 0 && size <= maxSize;
    }

    /// <summary>
    /// Valida formato de chave S3
    /// </summary>
    public static bool IsValidS3Key(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        
        // Não permitir path traversal
        if (key.Contains("..")) return false;
        if (key.StartsWith("/")) return false;
        
        // Tamanho máximo
        if (key.Length > 1024) return false;
        
        return true;
    }
}
