using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using AspecCapturaApi.Models;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AspecCapturaApi.Configuration;
using AspecCapturaApi.Services;
using AspecCapturaApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

// ─── Carregar variáveis de ambiente do arquivo .env ───────────────────────────
EnvironmentHelper.LoadDotEnv();

// ─── Mapear variáveis .env para o formato AWS__ do ASP.NET Core ───────────────
void MapEnvToAspNet(string envKey, string aspNetKey)
{
    var val = Environment.GetEnvironmentVariable(envKey);
    if (!string.IsNullOrEmpty(val))
        Environment.SetEnvironmentVariable(aspNetKey, val);
}
MapEnvToAspNet("AWS_REGION",            "AWS__Region");
MapEnvToAspNet("AWS_BUCKET_NAME",       "AWS__BucketName");
MapEnvToAspNet("AWS_ACCESS_KEY_ID",     "AWS__AccessKey");
MapEnvToAspNet("AWS_SECRET_ACCESS_KEY", "AWS__SecretKey");
MapEnvToAspNet("JWT_SECRET",            "Security__JwtSecret");
MapEnvToAspNet("JWT_ISSUER",            "Security__JwtIssuer");
MapEnvToAspNet("JWT_AUDIENCE",          "Security__JwtAudience");
MapEnvToAspNet("JWT_EXPIRATION_MINUTES","Security__JwtExpirationMinutes");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Se a variável de ambiente não estiver definida, assume "Development"
    // Isso garante que appsettings.Development.json seja carregado corretamente
    EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
});

// ─── Logging Estruturado ───────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ─── Serviços ─────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ASPEC Capture API", Version = "0.11.1" });
});

// ─── AWS ──────────────────────────────────────────────────────────────────────
var awsOptions = builder.Configuration.GetAWSOptions();

// ─── SECURITY: Ler credenciais do ambiente (prioridade) ou fallback para configuração ───
var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") 
    ?? builder.Configuration["AWS:AccessKey"];
    
var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") 
    ?? builder.Configuration["AWS:SecretKey"];

if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
{
    awsOptions.Credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);
    Console.WriteLine("✅ AWS credentials loaded from environment variables");
}
else
{
    // Em produção, usar AWS SDK default credential chain (IAM roles, instance profiles, etc.)
    Console.WriteLine("⚠️  AWS credentials not found in environment. Using default credential chain.");
}

builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var config = new Amazon.S3.AmazonS3Config
    {
        RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(
            Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-2"),
        Timeout = TimeSpan.FromSeconds(60),
        MaxErrorRetry = 2,
        UseAccelerateEndpoint = true  // Roteia via edge location mais próxima (reduz latência Brasil→Ohio)
    };
    var credentials = new Amazon.Runtime.BasicAWSCredentials(
        Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? builder.Configuration["AWS:AccessKey"],
        Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? builder.Configuration["AWS:SecretKey"]
    );
    return new Amazon.S3.AmazonS3Client(credentials, config);
});

// ─── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", corsBuilder =>
    {
        corsBuilder
            .SetIsOriginAllowed(origin =>
            {
                if (builder.Environment.IsDevelopment()) return true;
                
                // Permite o domínio principal e todos os subdomínios do Cloudflare Pages
                // Formato: https://[hash].pwa-camera-poc-blazor.pages.dev ou https://pwa-camera-poc-blazor.pages.dev
                if (string.IsNullOrEmpty(origin)) return false;
                
                var uri = new Uri(origin);
                var host = uri.Host.ToLowerInvariant();
                
                return host == "pwa-camera-poc-blazor.pages.dev" ||
                       host.EndsWith(".pwa-camera-poc-blazor.pages.dev") ||
                       host.Contains("pwa-camera-poc-blazor.pages.dev");
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// ─── JSON Options globais (case-insensitive) ──────────────────────────────────
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
};

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddMemoryCache();

// Configuração de JSON para Minimal APIs
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.AllowTrailingCommas = true;
    options.SerializerOptions.ReadCommentHandling = JsonCommentHandling.Skip;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.MaxDepth = 0; // sem limite de profundidade
        options.JsonSerializerOptions.DefaultBufferSize = 16 * 1024 * 1024; // 16 MB, ajuste conforme necessário
    });

// ─── SECURITY: Authentication & Authorization ─────────────────────────────────
// Registrar AuthService
builder.Services.AddScoped<IAuthService, AuthService>();

// Configurar JWT Authentication
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") 
    ?? throw new InvalidOperationException("JWT_SECRET not configured in .env file");

if (jwtSecret.Length < 32)
{
    Console.WriteLine("⚠️  WARNING: JWT_SECRET is too short. Use at least 256 bits (32 characters) for production!");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "aspec-capture-api",
            ValidateAudience = true,
            ValidAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "aspec-capture-client",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero // Sem tolerância de tempo para expiração
        };
    });

builder.Services.AddAuthorization();


var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.UseResponseCompression();

// ─── Pipeline ─────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ASPEC Capture API v1");
        c.RoutePrefix = "swagger";
        c.DisplayRequestDuration();
    });
}

// Middleware global de exceções
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (feature?.Error is { } ex)
        {
            logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await context.Response.WriteAsJsonAsync(new { error = "Erro interno do servidor.", detail = app.Environment.IsDevelopment() ? ex.Message : null });
        }
    });
});

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("AllowSpecificOrigins");

// ─── SECURITY: Middlewares ────────────────────────────────────────────────────
app.UseRequestLogging();      // Log de requisições (sem dados sensíveis)
app.UseSecurityHeaders();     // Headers de segurança (XSS, Clickjacking, etc)
app.UseAuthentication();      // Autenticação JWT
app.UseAuthorization();       // Autorização baseada em claims

// Redirect root to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"));

// ─── ENDPOINT: Health Check (Render) ─────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithOpenApi()
    .WithTags("Health")
    .ExcludeFromDescription();

// ─── Auto-configure S3 CORS (startup) ────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var bucketName = config["AWS:BucketName"];
    var region = config["AWS:Region"];

    if (!string.IsNullOrEmpty(bucketName) && bucketName != "AWS__BucketName" &&
        !string.IsNullOrEmpty(region) && region != "AWS__Region")
    {
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
        await s3.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
        {
            BucketName = bucketName,
            Configuration = new CORSConfiguration
            {
                Rules =
                [
                    new CORSRule
                    {
                        AllowedMethods = ["GET", "PUT", "POST", "HEAD", "DELETE"],
                        AllowedOrigins = ["*"],
                        AllowedHeaders = ["*"],
                        ExposeHeaders = ["ETag", "x-amz-meta-asset-code"],
                        MaxAgeSeconds = 3000
                    }
                ]
            }
        });
        logger.LogInformation("S3 CORS configurado para bucket {Bucket}", bucketName);
    }
    else
    {
        logger.LogWarning("AWS não configurado completamente. Pulando S3 CORS auto-config.");
    }
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Falha ao configurar S3 CORS: {Message}", ex.Message);
}

// ─── ENDPOINT: Presigned URL ──────────────────────────────────────────────────
app.MapPost("/api/storage/presigned-url", async (
    [FromBody] PresignedUrlRequest request,
    [FromServices] IAmazonS3 s3Client,
    [FromServices] IConfiguration configuration,
    ILogger<Program> log) =>
{
    var bucketName = configuration["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucketName))
        return Results.Problem("AWS BucketName não configurado.");

    static string SanitizeKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var normalized = input.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString()
                 .Normalize(System.Text.NormalizationForm.FormC)
                 .Replace(" ", "-")
                 .Replace("%20", "-")
                 .Replace("/", "-")
                 .Replace("\\", "-");
    }

    var rawFolder = !string.IsNullOrEmpty(request.AssetId)
        ? Uri.UnescapeDataString(request.AssetId)
        : "temp";

    var sanitizedFolder = string.Join("/", rawFolder.Split('/').Select(SanitizeKey));
    var sanitizedFileName = SanitizeKey(request.FileName);
    var key = $"{sanitizedFolder}/{sanitizedFileName}";

    log.LogDebug("Gerando Pre-signed URL para chave: {Key}", key);

    var presignedUrlRequest = new GetPreSignedUrlRequest
    {
        BucketName = bucketName,
        Key = key,
        Verb = HttpVerb.PUT,
        Expires = DateTime.UtcNow.AddMinutes(10),
        ContentType = request.ContentType
    };

    if (!string.IsNullOrEmpty(request.AssetCode))
    {
        log.LogDebug("Assinando com asset-code: {AssetCode}", request.AssetCode);
        presignedUrlRequest.Metadata.Add("asset-code", request.AssetCode);
    }

    try
    {
        var url = s3Client.GetPreSignedURL(presignedUrlRequest);
        return Results.Ok(new PresignedUrlResponse(url, key));
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro ao gerar Pre-signed URL para {Key}", key);
        return Results.Problem($"Erro ao gerar URL: {ex.Message}");
    }
})
.WithName("GetPresignedUrl")
.WithOpenApi()
.WithTags("Storage");

// ─── ENDPOINT: Verifica existência no S3 ─────────────────────────────────────
app.MapGet("/api/storage/exists/{*filePath}", async (
    string filePath,
    [FromServices] IAmazonS3 s3Client,
    [FromServices] IConfiguration configuration,
    ILogger<Program> log) =>
{
    var bucketName = configuration["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucketName))
        return Results.Problem("Bucket não configurado.");

    try
    {
        await s3Client.GetObjectMetadataAsync(bucketName, filePath);
        var region = configuration["AWS:Region"] ?? "us-east-1";
        var url = $"https://{bucketName}.s3.{region}.amazonaws.com/{filePath}";
        return Results.Ok(new { exists = true, key = filePath, url });
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.Ok(new { exists = false, key = filePath });
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
    {
        log.LogWarning("S3 Proibido para chave '{FilePath}': {Message}", filePath, ex.Message);
        return Results.Problem($"Acesso negado para: {filePath}", statusCode: 403);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro S3 ao verificar existência de {FilePath}", filePath);
        return Results.Problem(ex.Message);
    }
})
.WithName("CheckObjectExists")
.WithOpenApi()
.WithTags("Storage");

app.MapGet("/api/tombamentos/localizacoes", async (
    [FromQuery] string prefix,
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    [FromServices] IMemoryCache cache,
    ILogger<Program> log) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return Results.BadRequest(new { error = "prefix é obrigatório" });

        var upper = prefix.Trim().ToUpperInvariant();
        var key = $"usuarios/{upper}.json";

        var bucket = config["AWS:BucketName"];
        if (string.IsNullOrEmpty(bucket))
            return Results.Problem("Bucket não configurado.");

        var head = await s3.GetObjectMetadataAsync(bucket, key);
        var ver = (head.LastModified ?? DateTime.UtcNow).ToUniversalTime().ToString("yyyyMMddHHmmss");
        var cacheKey = $"TOMB-LOC:{bucket}:{key}:{ver}";

        if (!cache.TryGetValue(cacheKey, out List<object>? cached))
        {
            // Usa GetOrLoadUnifiedDataAsync — reutiliza cache TOMB-DATA se já existir
            var unified = await GetOrLoadUnifiedDataAsync(s3, bucket, key, ver, cache, jsonOptions, log);

            var locs = unified.Tabelas?.Localizacao ?? new List<LocalizacaoRecord>();
            var best = locs
                .GroupBy(l => l.IdLocalizacao)
                .Select(g => g
                    .OrderByDescending(l => l.DtEstr)
                    .ThenBy(l => l.CdOrgao == "99" ? 1 : 0)
                    .ThenBy(l => l.CdOrgao)
                    .ThenBy(l => l.CdUnid)
                    .First())
                .ToList();

            cached = best.Select(l => (object)new
            {
                idlocalizacao = l.IdLocalizacao,
                cdorgao = l.CdOrgao,
                cdunid = l.CdUnid,
                cdarea = l.CdArea,
                cdsarea = l.CdSArea
            }).ToList();
            cache.Set(cacheKey, cached, TimeSpan.FromHours(1));
        }

        return Results.Ok(cached);
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = $"Prefixo '{prefix}' não encontrado." });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro ao listar localizacoes para {Prefix}", prefix);
        return Results.Problem(ex.Message);
    }
})
.WithName("GetLocalizacoes")
.WithOpenApi()
.WithTags("Tombamentos");


// ─── ENDPOINT: Login ──────────────────────────────────────────────────────────
app.MapPost("/api/auth/login", async (
    [FromBody] LoginRequest request,
    [FromServices] IAmazonS3 s3Client,
    [FromServices] IConfiguration configuration,
    [FromServices] IAuthService authService,
    [FromServices] IWebHostEnvironment env,
    ILogger<Program> log) =>
{
    var usuario = request.Usuario?.Trim() ?? string.Empty;
    var senha = request.Senha?.Trim() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha))
        return Results.BadRequest(new { error = "Usuário e Senha são obrigatórios." });

    // Formato esperado: ce999.nome1.sbnome1
    var parts = usuario.Split('.', 2);
    if (parts.Length < 2)
    {
        log.LogWarning("Formato de usuário inválido: {Usuario}", usuario);
        return Results.BadRequest(new { error = "Formato de usuário inválido. Use: municipio.usuario" });
    }

    var prefix = parts[0].ToUpper();
    var s3Key = $"usuarios/{prefix}.json";
    var s3KeyLower = $"usuarios/{parts[0].ToLower()}.json";

    log.LogInformation("Tentativa de login para município {Prefix}, usuário {Usuario}", prefix, usuario);

    try
    {
        // Carrega dados de usuário do S3
        var bucketName = Environment.GetEnvironmentVariable("AWS_BUCKET_NAME")
            ?? configuration["AWS:BucketName"];
        if (string.IsNullOrEmpty(bucketName) || bucketName == "AWS__BucketName")
            return Results.Problem("Bucket não configurado.");

        log.LogInformation("Buscando arquivo S3: {S3Key} no bucket {Bucket}", s3Key, bucketName);
        using Amazon.S3.Model.GetObjectResponse s3Response = await GetS3ObjectWithFallback(s3Client, bucketName, s3Key, s3KeyLower, log);
        log.LogInformation("Arquivo S3 carregado com sucesso: {S3Key}", s3Key);

        // Copia o stream S3 para MemoryStream antes de fechar o response
        // Evita travamento em streams de rede que ficam pendentes durante leitura longa
        byte[] rawBytes;
        using (var responseStream = s3Response.ResponseStream)
        {
            using var ms = new MemoryStream();
            await responseStream.CopyToAsync(ms);
            rawBytes = ms.ToArray();
        }
        log.LogInformation("Login: {S3Key} lido ({Bytes} bytes)", s3Key, rawBytes.Length);

        // Decodifica com fallback tolerante para bytes inválidos (encoding legado Harbour)
        var tolerantEncoding = Encoding.GetEncoding("UTF-8",
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);
        var json = tolerantEncoding.GetString(rawBytes);
        var root = JsonSerializer.Deserialize<UnifiedDataRecord>(json, jsonOptions);

        if (root?.Usuarios == null || root.Usuarios.Count == 0)
        {
            log.LogWarning("Arquivo {S3Key} não contém usuários válidos.", s3Key);
            return Results.Unauthorized();
        }
        var restOfUsername = parts[1].ToLower();
        var user = root.Usuarios.FirstOrDefault(u =>
            string.Equals(u.NmUsuario, usuario, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.NmUsuario, restOfUsername, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            log.LogWarning("Usuário '{Usuario}' não encontrado.", usuario);
            return Results.Unauthorized();
        }
        
        // ─── SECURITY: Validar senha usando AuthService ──────────────────────────
        if (!authService.ValidatePassword(senha, user.PwdUsuario))
        {
            log.LogWarning("Authentication failed for user '{Usuario}' from municipality {Prefix}", usuario, prefix);
            return Results.Unauthorized();
        }
        
        log.LogInformation("Login bem-sucedido: {Usuario}, Esfera: {Esfera}", user.NmUsuario, user.Esfera);

        // 2. Carrega patrimônio completo do arquivo {prefix}_01.json
        // var patrimonioKey = $"patrimonio/{prefix}_01.json";
        // List<PatrimonioCargaItem> patrimonioCarga = new();
        // try
        // {
        //     using var patrResp = await s3Client.GetObjectAsync(bucketName, patrimonioKey);
        //     using var patrReader = new StreamReader(patrResp.ResponseStream);
        //     var patrJson = await patrReader.ReadToEndAsync();
        //     patrimonioCarga = JsonSerializer.Deserialize<List<PatrimonioCargaItem>>(patrJson, jsonOptions) ?? new();
        // }
        // catch (Exception ex)
        // {
        //     log.LogWarning(ex, "Falha ao carregar patrimônio completo do arquivo {PatrimonioKey}", patrimonioKey);
        // }

        // 3. Obtém dados das tabelas
        var tabelas = root.Tabelas;
        if (tabelas is null)
        {
            log.LogWarning("Seção 'tabelas' ausente no arquivo {S3Key}.", s3Key);
            return Results.Problem("Dados de tabelas ausentes no arquivo do município.");
        }

        // 4. Filtra patrimônio para a esfera do usuário
        var tombamentoBase = tabelas.Tombamentos ?? new List<TombamentoRecord>();
        
        log.LogInformation("Tombamentos encontrados em tabelas.tombamentos: {Count}", tombamentoBase.Count);

        // NOVO: Filtro de exercício fiscal corrente
        var exercicioCorrente = DateTime.Now.Year;
        var tombamentoFiltradoExercicio = tombamentoBase
            .Where(p => p.ExercicioFiscal == exercicioCorrente || p.ExercicioFiscal == 0)
            .ToList();

        log.LogInformation(
            "Tombamentos filtrados para exercício {Exercicio}: {Count} (de {Total})", 
            exercicioCorrente, 
            tombamentoFiltradoExercicio.Count, 
            tombamentoBase.Count
        );

        // Filtro de esfera
        var tombamentoFiltrado = user.Esfera == "A"
            ? tombamentoFiltradoExercicio
            : tombamentoFiltradoExercicio.Where(p => p.Esfera == user.Esfera).ToList();

        log.LogInformation("Tombamentos filtrados para esfera {Esfera}: {Count}", user.Esfera, tombamentoFiltrado.Count);

        // 5. Filtra patrimônio para árvore de órgãos (dados de apoio)
        var orgaos = BuildOrgaoHierarchy(user.Esfera, tombamentoFiltrado, tabelas);

        var tombamentos = Enumerable.Empty<TombamentoItemResponse>().AsQueryable();

        // ─── SECURITY: Gerar token JWT ────────────────────────────────────────────
        var token = authService.GenerateJwtToken(
            userId: user.IdUsuario?.ToString() ?? Guid.NewGuid().ToString(),
            username: user.NmUsuario,
            prefix: prefix,
            esfera: user.Esfera
        );

        return Results.Stream(async stream =>
        {
            await JsonSerializer.SerializeAsync(stream, new AuthResponse(
                NomeCompleto: user.NomeCompleto ?? user.NmUsuario,
                Prefixo: prefix,
                Esfera: user.Esfera,
                Orgaos: orgaos,
                Tombamentos: tombamentos.ToList(),
                Token: token  // ✅ Usar token JWT real, não Guid
            ), jsonOptions); // Passamos jsonOptions aqui!
        }, "application/json");
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        log.LogWarning("Arquivo '{S3Key}' não encontrado no S3.", s3Key);
        return Results.NotFound(new { error = $"Município '{prefix}' não encontrado." });
    }
    catch (JsonException ex)
    {
        log.LogError(ex, "Erro ao deserializar JSON do arquivo {S3Key}", s3Key);
        return Results.Problem($"Formato de dados inválido para o município '{prefix}'.");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro inesperado no login para {Usuario}", request.Usuario);
        return Results.Problem("Erro interno ao autenticar.");
    }
})
.WithName("Login")
.WithOpenApi()
.WithTags("Auth");

app.MapGet("/api/tombamentos/sync-info", async (
    [FromQuery] string prefix,
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    [FromServices] IMemoryCache cache,
    ILogger<Program> log) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            log.LogWarning("SyncInfo: prefix vazio ou nulo");
            return Results.BadRequest(new { error = "prefix é obrigatório" });
        }
        
        var key = $"usuarios/{prefix.ToUpper()}.json";
        log.LogInformation("SyncInfo: Processando key={Key}", key);

        var bucket = config["AWS:BucketName"];
        if (string.IsNullOrEmpty(bucket))
        {
            log.LogError("SyncInfo: Bucket não configurado");
            return Results.Problem("Bucket não configurado.");
        }

        // Verifica se o arquivo existe antes de processar
        try
        {
            await s3.GetObjectMetadataAsync(bucket, key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            log.LogWarning("SyncInfo: Arquivo não encontrado: {Key}", key);
            return Results.NotFound(new { error = $"Arquivo {key} não encontrado no bucket {bucket}" });
        }

        var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache, log);
        
        log.LogInformation("SyncInfo: Sucesso - {TotalRegistros} registros, {TotalChunks} chunks", 
            index.TotalRecords, index.Chunks.Count);
        
        return Results.Ok(new
        {
            totalRegistros = index.TotalRecords,
            totalChunks = index.Chunks.Count,
            versao = index.Version,
            hashGlobal = index.GlobalHash
        });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "SyncInfo: Erro ao processar sync-info para prefix={Prefix}", prefix);
        return Results.Problem(
            title: "Erro ao processar sync-info",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.WithName("TombamentosSyncInfo")
.WithOpenApi()
.WithTags("Tombamentos");

app.MapGet("/api/tombamentos/lote/{id:int}", async (
    int id,
    [FromQuery] string prefix,
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    [FromServices] IMemoryCache cache,
    ILogger<Program> log) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            log.LogWarning("Lote: prefix vazio ou nulo");
            return Results.BadRequest(new { error = "prefix é obrigatório" });
        }
        
        var key = $"usuarios/{prefix.ToUpper()}.json";
        log.LogInformation("Lote: Processando chunk {ChunkId} para key={Key}", id, key);

        var bucket = config["AWS:BucketName"];
        if (string.IsNullOrEmpty(bucket))
        {
            log.LogError("Lote: Bucket não configurado");
            return Results.Problem("Bucket não configurado.");
        }

        var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache, log);
        
        if (id < 1 || id > index.Chunks.Count)
        {
            log.LogWarning("Lote: ChunkId {ChunkId} inválido (total: {Total})", id, index.Chunks.Count);
            return Results.NotFound(new { error = $"chunkId {id} inválido (total: {index.Chunks.Count})" });
        }
        
        var chunkMeta = index.Chunks[id - 1];
        var cacheKey = $"TOMB-CHUNK:{bucket}:{key}:{index.Version}:{id}";
        
        if (!cache.TryGetValue<byte[]>(cacheKey, out var payload))
        {
            log.LogInformation("Lote: Gerando payload para chunk {ChunkId}", id);
            // Obtém o UnifiedDataRecord do cache (ou S3 se não estiver em cache)
            // BuildOrGetChunkIndexAsync já garantiu que a versão está disponível
            var head = await s3.GetObjectMetadataAsync(bucket, key);
            var version2 = (head.LastModified ?? DateTime.UtcNow).ToUniversalTime().ToString("yyyyMMddHHmmss");
            var unifiedData = await GetOrLoadUnifiedDataAsync(s3, bucket, key, version2, cache, index.Options, log);
            payload = await SerializeChunkPayloadAsync(unifiedData, chunkMeta, index.Options);
            cache.Set(cacheKey, payload, TimeSpan.FromHours(1));
        }
        else
        {
            log.LogInformation("Lote: Usando payload em cache para chunk {ChunkId}", id);
        }
        
        return Results.Bytes(payload!, "application/json");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Lote: Erro ao processar chunk {ChunkId} para prefix={Prefix}", id, prefix);
        return Results.Problem(
            title: "Erro ao processar lote",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.WithName("TombamentosChunk")
.WithOpenApi()
.WithTags("Tombamentos");

// ─── ENDPOINT: POST /api/capture/item ────────────────────────────────────────
app.MapPost("/api/capture/item", 
    [Authorize] async (  // ✅ Requer autenticação
    [FromBody] CaptureItemRequest request,
    ClaimsPrincipal user,  // ✅ Recebe usuário autenticado
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    [FromServices] IMemoryCache cache,
    ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(request.Prefixo))
        return Results.BadRequest(new { error = "prefixo é obrigatório." });

    // ─── SECURITY: Validar autorização (usuário só acessa seu município) ─────
    var userPrefix = user.FindFirst("prefix")?.Value;
    if (userPrefix != request.Prefixo)
    {
        log.LogWarning("Authorization failed: user prefix '{UserPrefix}' != request prefix '{RequestPrefix}'", 
            userPrefix, request.Prefixo);
        return Results.Forbid();
    }

    var bucket = config["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucket))
        return Results.Problem("Bucket não configurado.");

    var s3Key = $"usuarios/{request.Prefixo.ToUpper()}.json";
    var writeOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    try
    {
        // 1. Carregar arquivo completo
        UnifiedDataRecord root;
        using (var obj = await s3.GetObjectAsync(bucket, s3Key))
        using (var stream = obj.ResponseStream)
        {
            root = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, writeOptions)
                   ?? throw new InvalidOperationException("Arquivo inválido.");
        }

        var tombamentos = root.Tabelas?.Tombamentos
            ?? throw new InvalidOperationException("Seção tombamentos ausente.");

        // 2. Localizar tombamento por idpatomb (fallback: nutomb)
        var existing = tombamentos.FirstOrDefault(t => t.IdPatomb == request.IdPatomb)
                    ?? tombamentos.FirstOrDefault(t => t.Nutomb == request.Nutomb);

        var today = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
        string operationStatus;

        if (existing is not null)
        {
            // 3a. Atualizar campos de captura
            existing.Estado = request.Estado ?? existing.Estado;
            existing.Dataestado = today;
            existing.Situacao = request.Situacao ?? existing.Situacao;
            existing.Datasituacao = today;
            existing.IdLocalizacao = request.IdLocalizacao ?? existing.IdLocalizacao;
            existing.FotoKey = request.FotoKey ?? existing.FotoKey;
            existing.CapturedBy = request.CapturedBy;
            existing.CapturedAt = request.CapturedAt ?? DateTime.UtcNow.ToString("o");
            existing.Source = request.Source;
            operationStatus = "updated";
            log.LogInformation("Tombamento {IdPatomb} atualizado.", existing.IdPatomb);
        }
        else
        {
            // 3b. Adicionar novo tombamento
            var novo = new TombamentoRecord
            {
                IdPatomb = request.IdPatomb,
                Nutomb = request.Nutomb,
                Estado = request.Estado,
                Dataestado = today,
                Situacao = request.Situacao,
                Datasituacao = today,
                IdLocalizacao = request.IdLocalizacao,
                FotoKey = request.FotoKey,
                CapturedBy = request.CapturedBy,
                CapturedAt = request.CapturedAt ?? DateTime.UtcNow.ToString("o"),
                Source = request.Source
            };
            tombamentos.Add(novo);
            existing = novo;
            operationStatus = "created";
            log.LogInformation("Tombamento {IdPatomb} criado.", novo.IdPatomb);
        }

        // 4. Serializar e fazer PutObject
        var updatedJson = JsonSerializer.SerializeToUtf8Bytes(root, writeOptions);
        using var ms = new MemoryStream(updatedJson);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = s3Key,
            InputStream = ms,
            ContentType = "application/json"
        });

        // 5. Invalidar cache do chunk index e dados do município
        await InvalidateCacheForPrefixAsync(s3, bucket, s3Key, cache, log);

        return Results.Ok(new CaptureItemResponse(
            IdPatomb: existing.IdPatomb,
            Nutomb: existing.Nutomb,
            Status: operationStatus,
            UpdatedAt: DateTime.UtcNow.ToString("o")
        ));
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        log.LogWarning("Arquivo {S3Key} não encontrado.", s3Key);
        return Results.NotFound(new { error = $"Prefixo '{request.Prefixo}' não encontrado." });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro ao salvar captura para {Prefixo}", request.Prefixo);
        return Results.Problem(ex.Message);
    }
})
.WithName("CaptureItem")
.WithOpenApi()
.WithTags("Capture")
.RequireAuthorization();  // ✅ Garante que autenticação seja aplicada

// ─── ENDPOINT: GET /api/capture/validate/{nutomb} ────────────────────────────
app.MapGet("/api/capture/validate/{nutomb}", async (
    string nutomb,
    [FromQuery] string? prefix,
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    [FromServices] IMemoryCache cache,
    ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(prefix))
        return Results.BadRequest(new { error = "prefix é obrigatório." });

    var bucket = config["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucket))
        return Results.Problem("Bucket não configurado.");

    var s3Key = $"usuarios/{prefix.ToUpper()}.json";

    try
    {
        var readOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        UnifiedDataRecord root;
        using (var obj = await s3.GetObjectAsync(bucket, s3Key))
        using (var stream = obj.ResponseStream)
        {
            root = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, readOptions)
                   ?? throw new InvalidOperationException("Arquivo inválido.");
        }

        var tombamento = root.Tabelas?.Tombamentos?
            .FirstOrDefault(t => t.Nutomb == nutomb);

        if (tombamento is null)
        {
            return Results.Ok(new ValidateTombamentoResponse(
                Exists: false, IdPatomb: null, Nutomb: nutomb,
                Esfera: null, Deprod: null, Cdprod: null,
                Estado: null, Situacao: null, IdLocalizacao: null));
        }

        return Results.Ok(new ValidateTombamentoResponse(
            Exists: true,
            IdPatomb: tombamento.IdPatomb,
            Nutomb: tombamento.Nutomb,
            Esfera: tombamento.Esfera,
            Deprod: tombamento.Deprod,
            Cdprod: tombamento.Cdprod,
            Estado: tombamento.Estado,
            Situacao: tombamento.Situacao,
            IdLocalizacao: tombamento.IdLocalizacao));
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = $"Prefixo '{prefix}' não encontrado." });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro ao validar tombamento {Nutomb}", nutomb);
        return Results.Problem(ex.Message);
    }
})
.WithName("ValidateTombamento")
.WithOpenApi()
.WithTags("Capture");

// ─── ENDPOINT: GET /api/capture/list ─────────────────────────────────────────
app.MapGet("/api/capture/list", 
    [Authorize] async (  // ✅ Requer autenticação
    [FromQuery] string prefix,
    [FromQuery] string? cdorgao,
    [FromQuery] string? cdunid,
    ClaimsPrincipal user,  // ✅ Recebe usuário autenticado
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(prefix))
        return Results.BadRequest(new { error = "prefix é obrigatório." });

    // ─── SECURITY: Validar autorização (usuário só acessa seu município) ─────
    var userPrefix = user.FindFirst("prefix")?.Value;
    if (userPrefix != prefix.ToUpper())
    {
        log.LogWarning("Authorization failed on list: user prefix '{UserPrefix}' != request prefix '{RequestPrefix}'", 
            userPrefix, prefix);
        return Results.Forbid();
    }

    var bucket = config["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucket))
        return Results.Problem("Bucket não configurado.");

    var s3Key = $"usuarios/{prefix.ToUpper()}.json";

    try
    {
        var readOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        UnifiedDataRecord root;
        using (var obj = await s3.GetObjectAsync(bucket, s3Key))
        using (var stream = obj.ResponseStream)
        {
            root = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, readOptions)
                   ?? throw new InvalidOperationException("Arquivo inválido.");
        }

        var tombamentos = root.Tabelas?.Tombamentos ?? new List<TombamentoRecord>();

        // Filtrar apenas os que foram capturados (situacao != null)
        var query = tombamentos.Where(t => t.Situacao != null);

        if (!string.IsNullOrWhiteSpace(cdorgao))
            query = query.Where(t => t.CdOrgao == cdorgao);

        if (!string.IsNullOrWhiteSpace(cdunid))
            query = query.Where(t => t.CdUnid == cdunid);

        var result = query.Select(t => new CapturedItemSummary(
            IdPatomb: t.IdPatomb,
            Nutomb: t.Nutomb,
            Deprod: t.Deprod,
            Estado: t.Estado,
            Situacao: t.Situacao,
            Esfera: t.Esfera
        )).ToList();

        return Results.Ok(result);
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = $"Prefixo '{prefix}' não encontrado." });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro ao listar capturas para {Prefix}", prefix);
        return Results.Problem(ex.Message);
    }
})
.WithName("ListCapturedItems")
.WithOpenApi()
.WithTags("Capture")
.RequireAuthorization();  // ✅ Garante que autenticação seja aplicada

// ─── ENDPOINT: POST /api/capture/sync ────────────────────────────────────────
app.MapPost("/api/capture/sync", 
    [Authorize] async (  // ✅ Requer autenticação
    [FromBody] SyncBatchRequest request,
    ClaimsPrincipal user,  // ✅ Recebe usuário autenticado
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    [FromServices] IMemoryCache cache,
    ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(request.Prefixo))
        return Results.BadRequest(new { error = "prefixo é obrigatório." });

    // ─── SECURITY: Validar autorização (usuário só acessa seu município) ─────
    var userPrefix = user.FindFirst("prefix")?.Value;
    if (userPrefix != request.Prefixo)
    {
        log.LogWarning("Authorization failed on sync: user prefix '{UserPrefix}' != request prefix '{RequestPrefix}'", 
            userPrefix, request.Prefixo);
        return Results.Forbid();
    }

    if (request.Items == null || request.Items.Count == 0)
        return Results.Ok(new SyncBatchResponse(0, 0, 0, 0, new List<SyncItemResult>()));

    if (request.Items.Count > 50)
        return Results.BadRequest(new { error = "Máximo de 50 itens por batch." });

    var bucket = config["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucket))
        return Results.Problem("Bucket não configurado.");

    var s3Key = $"usuarios/{request.Prefixo.ToUpper()}.json";
    var writeOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    try
    {
        // 1. Carregar arquivo uma única vez
        UnifiedDataRecord root;
        using (var obj = await s3.GetObjectAsync(bucket, s3Key))
        using (var stream = obj.ResponseStream)
        {
            root = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, writeOptions)
                   ?? throw new InvalidOperationException("Arquivo inválido.");
        }

        var tombamentos = root.Tabelas?.Tombamentos
            ?? throw new InvalidOperationException("Seção tombamentos ausente.");

        var results = new List<SyncItemResult>();
        int updated = 0, created = 0, failed = 0;
        var today = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));

        // 2. Processar cada item em memória
        foreach (var item in request.Items)
        {
            try
            {
                var existing = tombamentos.FirstOrDefault(t => t.IdPatomb == item.IdPatomb)
                            ?? tombamentos.FirstOrDefault(t => t.Nutomb == item.Nutomb);

                string opStatus;
                if (existing is not null)
                {
                    existing.Estado = item.Estado ?? existing.Estado;
                    existing.Dataestado = today;
                    existing.Situacao = item.Situacao ?? existing.Situacao;
                    existing.Datasituacao = today;
                    existing.IdLocalizacao = item.IdLocalizacao ?? existing.IdLocalizacao;
                    existing.FotoKey = item.FotoKey ?? existing.FotoKey;
                    existing.CapturedBy = item.CapturedBy;
                    existing.CapturedAt = item.CapturedAt ?? DateTime.UtcNow.ToString("o");
                    existing.Source = item.Source;
                    opStatus = "updated";
                    updated++;
                }
                else
                {
                    tombamentos.Add(new TombamentoRecord
                    {
                        IdPatomb = item.IdPatomb,
                        Nutomb = item.Nutomb,
                        Estado = item.Estado,
                        Dataestado = today,
                        Situacao = item.Situacao,
                        Datasituacao = today,
                        IdLocalizacao = item.IdLocalizacao,
                        FotoKey = item.FotoKey,
                        CapturedBy = item.CapturedBy,
                        CapturedAt = item.CapturedAt ?? DateTime.UtcNow.ToString("o"),
                        Source = item.Source
                    });
                    opStatus = "created";
                    created++;
                }

                results.Add(new SyncItemResult(item.IdPatomb, item.Nutomb, opStatus));
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Falha ao processar item {IdPatomb}", item.IdPatomb);
                results.Add(new SyncItemResult(item.IdPatomb, item.Nutomb, "failed"));
                failed++;
            }
        }

        // 3. Um único PutObject para o batch inteiro
        var updatedJson = JsonSerializer.SerializeToUtf8Bytes(root, writeOptions);
        using var ms = new MemoryStream(updatedJson);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = s3Key,
            InputStream = ms,
            ContentType = "application/json"
        });

        // 4. Invalidar cache do chunk index e dados do município
        await InvalidateCacheForPrefixAsync(s3, bucket, s3Key, cache, log);

        log.LogInformation("Sync batch: {Updated} atualizados, {Created} criados, {Failed} falhas",
            updated, created, failed);

        return Results.Ok(new SyncBatchResponse(
            Total: request.Items.Count,
            Updated: updated,
            Created: created,
            Failed: failed,
            Results: results
        ));
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = $"Prefixo '{request.Prefixo}' não encontrado." });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Erro no sync batch para {Prefixo}", request.Prefixo);
        return Results.Problem(ex.Message);
    }
})
.WithName("SyncCaptureBatch")
.WithOpenApi()
.WithTags("Capture")
.RequireAuthorization();  // ✅ Garante que autenticação seja aplicada

app.Run();

// ─── Funções auxiliares ───────────────────────────────────────────────────────
static List<OrgaoRecord> BuildOrgaoHierarchy(
    string esfera,
    List<TombamentoRecord> filteredTombamento,
    TabelasRecord tabelas)
{
    // Pré-indexa tabelas em dicionários para evitar O(n²) com FirstOrDefault em listas grandes
    var orgaoPorChave = tabelas.XxOrga
        .GroupBy(o => (o.DtEstr, o.CdOrgao))
        .ToDictionary(g => g.Key, g => g.First());

    var unidPorChave = tabelas.XxUnid
        .GroupBy(u => (u.CdOrgao, u.CdUnid))
        .ToDictionary(g => g.Key, g => g.First());

    var areaPorCodigo = tabelas.PaArea
        .GroupBy(a => a.CdArea)
        .ToDictionary(g => g.Key, g => g.First());

    var subareaPorCodigo = tabelas.PasArea
        .GroupBy(s => s.CdSArea)
        .ToDictionary(g => g.Key, g => g.First());

    // Localização indexada por (CdOrgao, CdUnid) para lookup rápido
    var locPorOrgaoUnid = tabelas.Localizacao
        .GroupBy(l => (l.CdOrgao, l.CdUnid))
        .ToDictionary(g => g.Key, g => g.ToList());

    var orgaos = new List<OrgaoRecord>();

    var targetOrgaoKeys = esfera == "A"
        ? tabelas.XxOrga.Select(o => (o.DtEstr, o.CdOrgao)).Distinct()
        : filteredTombamento
            .Join(tabelas.XxOrga,
                  t => t.CdOrgao,
                  o => o.CdOrgao,
                  (t, o) => (o.DtEstr, t.CdOrgao))
            .Distinct();

    foreach (var (dtEstr, orgCode) in targetOrgaoKeys)
    {
        if (!orgaoPorChave.TryGetValue((dtEstr, orgCode), out var orgInfo)) continue;

        var targetUoCodes = esfera == "A"
            ? tabelas.XxUnid.Where(u => u.CdOrgao == orgCode).Select(u => u.CdUnid).Distinct()
            : filteredTombamento.Where(p => p.CdOrgao == orgCode).Select(p => p.CdUnid).Distinct();

        var uos = new List<UnidadeOrcamentariaRecord>();
        foreach (var uoCode in targetUoCodes)
        {
            if (!unidPorChave.TryGetValue((orgCode, uoCode), out var uoInfo)) continue;

            var locs = locPorOrgaoUnid.TryGetValue((orgCode, uoCode), out var locList)
                ? locList
                : new List<LocalizacaoRecord>();

            var targetAreaCodes = esfera == "A"
                ? locs.Select(l => l.CdArea).Distinct()
                : filteredTombamento
                    .Where(p => p.CdOrgao == orgCode && p.CdUnid == uoCode)
                    .Select(p => p.CdArea).Distinct();

            var areas = new List<AreaRecord>();
            foreach (var areaCode in targetAreaCodes)
            {
                if (!areaPorCodigo.TryGetValue(areaCode, out var areaInfo)) continue;

                var targetSubCodes = esfera == "A"
                    ? locs.Where(l => l.CdArea == areaCode).Select(l => l.CdSArea).Distinct()
                    : filteredTombamento
                        .Where(p => p.CdOrgao == orgCode && p.CdUnid == uoCode && p.CdArea == areaCode)
                        .Select(p => p.CdSArea).Distinct();

                var subareas = new List<SubareaRecord>();
                foreach (var subCode in targetSubCodes)
                {
                    if (!subareaPorCodigo.TryGetValue(subCode, out var subInfo)) continue;
                    subareas.Add(new SubareaRecord(subCode, subInfo.NmSArea));
                }

                areas.Add(new AreaRecord(areaCode, areaInfo.NmArea, subareas));
            }

            uos.Add(new UnidadeOrcamentariaRecord(uoCode, uoInfo.NmUnid, areas));
        }

        orgaos.Add(new OrgaoRecord(orgCode, orgInfo.NmOrgao, uos, dtEstr));
    }

    return orgaos;
}

static async Task<Amazon.S3.Model.GetObjectResponse> GetS3ObjectWithFallback(
    IAmazonS3 s3Client, 
    string bucket, 
    string primaryKey, 
    string fallbackKey, 
    ILogger log)
{
    try
    {
        return await s3Client.GetObjectAsync(bucket, primaryKey);
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        log.LogInformation("Arquivo '{PrimaryKey}' não encontrado, tentando '{FallbackKey}'", primaryKey, fallbackKey);
        return await s3Client.GetObjectAsync(bucket, fallbackKey);
    }
}

// ─── FUNÇÃO AUXILIAR: CARREGA E CACHEIA UnifiedDataRecord ────────────────────
/// <summary>
/// Única fonte de verdade para carregar o arquivo JSON do município do S3.
/// Cacheia o UnifiedDataRecord deserializado por 1 hora, eliminando downloads
/// repetidos para o mesmo arquivo durante a sessão de sincronização.
/// Chave de cache: TOMB-DATA:{bucket}:{key}:{version}
/// </summary>
static async Task<UnifiedDataRecord> GetOrLoadUnifiedDataAsync(
    IAmazonS3 s3, string bucket, string key, string version,
    IMemoryCache cache, JsonSerializerOptions options, ILogger? log = null)
{
    var cacheKey = $"TOMB-DATA:{bucket}:{key}:{version}";

    if (cache.TryGetValue(cacheKey, out UnifiedDataRecord? cached) && cached is not null)
    {
        log?.LogInformation("GetOrLoadUnifiedDataAsync: Cache hit para {Key} (versão {Version})", key, version);
        return cached;
    }

    log?.LogInformation("GetOrLoadUnifiedDataAsync: Baixando {Key} do S3 (versão {Version})", key, version);

    using var obj = await s3.GetObjectAsync(bucket, key);
    // Copia para MemoryStream antes de fechar o response — evita travamento em streams de rede longos
    byte[] rawBytes;
    using (var s3Stream = obj.ResponseStream)
    {
        using var ms = new MemoryStream();
        await s3Stream.CopyToAsync(ms);
        rawBytes = ms.ToArray();
    }
    var tolerantEncoding = Encoding.GetEncoding("UTF-8",
        EncoderFallback.ReplacementFallback,
        DecoderFallback.ReplacementFallback);
    var json = tolerantEncoding.GetString(rawBytes);
    var data = JsonSerializer.Deserialize<UnifiedDataRecord>(json, options)
               ?? throw new InvalidOperationException($"Arquivo inválido ou vazio: {key}");

    cache.Set(cacheKey, data, TimeSpan.FromHours(1));

    log?.LogInformation("GetOrLoadUnifiedDataAsync: {Key} carregado e cacheado ({Tombamentos} tombamentos)",
        key, data.Tabelas?.Tombamentos?.Count ?? 0);

    return data;
}

// ─── FUNÇÃO AUXILIAR: INVALIDA CACHE APÓS CAPTURA ────────────────────────────
/// <summary>
/// Remove as entradas de cache TOMB-INDEX e TOMB-DATA para o arquivo do município
/// após uma captura bem-sucedida (PutObjectAsync). Os chunks (TOMB-CHUNK) da versão
/// anterior expirarão naturalmente em 1 hora — o novo TOMB-INDEX gerado na próxima
/// requisição usará uma versão diferente, tornando as chaves antigas inacessíveis.
/// </summary>
static async Task InvalidateCacheForPrefixAsync(
    IAmazonS3 s3, string bucket, string s3Key,
    IMemoryCache cache, ILogger log)
{
    try
    {
        var head = await s3.GetObjectMetadataAsync(bucket, s3Key);
        var version = (head.LastModified ?? DateTime.UtcNow)
                        .ToUniversalTime().ToString("yyyyMMddHHmmss");

        var keysToRemove = new[]
        {
            $"TOMB-INDEX:{bucket}:{s3Key}:{version}",
            $"TOMB-DATA:{bucket}:{s3Key}:{version}",
        };

        foreach (var k in keysToRemove)
            cache.Remove(k);

        log.LogInformation(
            "Cache invalidado para {S3Key}: {Count} entradas removidas (versão {Version})",
            s3Key, keysToRemove.Length, version);
    }
    catch (Exception ex)
    {
        // Falha na invalidação não deve bloquear a resposta ao cliente
        log.LogWarning(ex, "Falha ao invalidar cache para {S3Key}: {Message}", s3Key, ex.Message);
    }
}

static async Task<ChunkIndex> BuildOrGetChunkIndexAsync(IAmazonS3 s3, string bucket, string key, IMemoryCache cache, ILogger? log = null)
{
    try
    {
        var head = await s3.GetObjectMetadataAsync(bucket, key);
        var dt = head.LastModified ?? DateTime.UtcNow;
        var version = dt.ToUniversalTime().ToString("yyyyMMddHHmmss");
        var cacheKey = $"TOMB-INDEX:{bucket}:{key}:{version}";
        
        if (cache.TryGetValue<ChunkIndex>(cacheKey, out var cached) && cached is not null)
        {
            log?.LogInformation("BuildOrGetChunkIndexAsync: Usando index em cache para {Key}", key);
            return cached;
        }
        
        log?.LogInformation("BuildOrGetChunkIndexAsync: Construindo index para {Key}", key);
        
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var chunks = new List<ChunkMeta>();
        var sha256 = SHA256.Create();
        var globalHashCtx = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var total = 0;
        var current = new List<TombamentoRecord>();
        
        const int MaxItemsPerChunk = 5000;
        
        var unifiedData = await GetOrLoadUnifiedDataAsync(s3, bucket, key, version, cache, options, log);
        var itens = unifiedData.Tabelas?.Tombamentos?.ToList();
        
        if (itens is null || itens.Count == 0)
        {
            log?.LogWarning("BuildOrGetChunkIndexAsync: Nenhum tombamento encontrado em {Key}", key);
            var emptyIndex = new ChunkIndex(version, 0, "", new List<ChunkMeta>(), options);
            cache.Set(cacheKey, emptyIndex, TimeSpan.FromHours(1));
            return emptyIndex;
        }

        var exercicioCorrente = DateTime.Now.Year;
        var itensFiltrados = itens
            .Where(p => p.ExercicioFiscal == exercicioCorrente || p.ExercicioFiscal == 0)
            .ToList();

        log?.LogInformation("BuildOrGetChunkIndexAsync: Processando {Total} tombamentos (filtrados: {Filtrados})", 
            itens.Count, itensFiltrados.Count);

        foreach (var item in itensFiltrados)
        {
            if (item is null) continue;
            current.Add(item);
            total++;
            
            if (current.Count >= MaxItemsPerChunk)
            {
                await FinalizeChunkAsync(current, total, sha256, globalHashCtx, chunks, options);
                current.Clear();
            }
        }
        
        if (current.Count > 0)
        {
            await FinalizeChunkAsync(current, total, sha256, globalHashCtx, chunks, options);
        }
        
        var globalHash = Convert.ToHexString(globalHashCtx.GetHashAndReset());
        var index = new ChunkIndex(version, total, globalHash, chunks, options);
        cache.Set(cacheKey, index, TimeSpan.FromHours(1));
        
        log?.LogInformation("BuildOrGetChunkIndexAsync: Index construído com sucesso - {Total} registros, {Chunks} chunks", 
            total, chunks.Count);
        
        return index;
    }
    catch (Exception ex)
    {
        log?.LogError(ex, "BuildOrGetChunkIndexAsync: Erro ao construir index para {Key}", key);
        throw;
    }
}

// ─── METODO AUXILIAR: FINALIZA CHUNK ─────────────────────────────────────────
static async Task FinalizeChunkAsync(
    List<TombamentoRecord> buffer, 
    int totalProcessed, 
    SHA256 sha256, 
    IncrementalHash globalHashCtx, 
    List<ChunkMeta> chunks, 
    JsonSerializerOptions options)
{
    // Serializa apenas uma vez quando o chunk esta completo
    var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(buffer, options);
    var hash = Convert.ToHexString(sha256.ComputeHash(jsonBytes));
    globalHashCtx.AppendData(Convert.FromHexString(hash));
    chunks.Add(new ChunkMeta(totalProcessed - buffer.Count + 1, buffer.Count, hash));
    
    // Log opcional para monitoramento
    Console.WriteLine($"[CHUNK] Finalizado: {buffer.Count} registros, {jsonBytes.Length / 1024} KB");
    await Task.CompletedTask; // Placeholder para operacoes async futuras
}

// ─── ESTRATEGIA 2: ESTIMATIVA DE TAMANHO (ALTERNATIVA) ───────────────────────
// Descomente para usar estimativa ao inves de batching fixo
/*
static int EstimateChunkSize(List<TombamentoRecord> sampleBuffer, int targetCompressedBytes, JsonSerializerOptions options)
{
    // Amostra os primeiros 100 registros para estimar tamanho medio
    const int SampleSize = 100;
    if (sampleBuffer.Count < SampleSize) return 5000; // Fallback
    
    var sample = sampleBuffer.Take(SampleSize).ToList();
    var sampleJson = JsonSerializer.SerializeToUtf8Bytes(sample, options);
    
    using var ms = new MemoryStream();
    using (var brotli = new BrotliStream(ms, CompressionLevel.Fastest))
    {
        brotli.Write(sampleJson);
    }
    
    var avgCompressedBytesPerRecord = ms.Length / SampleSize;
    var estimatedChunkSize = (int)(targetCompressedBytes / avgCompressedBytesPerRecord * 0.9); // 90% margem
    
    return Math.Max(1000, Math.Min(estimatedChunkSize, 10000)); // Entre 1k-10k
}
*/

// ─── ESTRATEGIA 3: ESCRITA INCREMENTAL (AVANCADA) ────────────────────────────
// Descomente para usar escrita incremental com Utf8JsonWriter
/*
static async Task<bool> ExceedsTargetIncrementalAsync(
    List<TombamentoRecord> buffer, 
    int targetCompressedBytes, 
    JsonSerializerOptions options)
{
    await using var ms = new MemoryStream();
    await using var brotli = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen: true);
    await using var writer = new Utf8JsonWriter(brotli, new JsonWriterOptions { Indented = false });
    
    writer.WriteStartArray();
    foreach (var record in buffer)
    {
        JsonSerializer.Serialize(writer, record, options);
    }
    writer.WriteEndArray();
    await writer.FlushAsync();
    await brotli.FlushAsync();
    
    return ms.Length >= targetCompressedBytes && buffer.Count > 0;
}
*/

// ─── METODO LEGADO (DEPRECATED) ──────────────────────────────────────────────
// AVISO: Este metodo causa O(n²) - NAO USAR EM PRODUCAO
// Mantido apenas como referencia historica do problema de performance
/*
static async Task<bool> ExceedsTargetAsync(List<TombamentoRecord> buffer, int targetCompressedBytes, JsonSerializerOptions options)
{
    // ⚠️ DEPRECATED: Causa gargalo de performance (14 min para 42 MB)
    // Use FinalizeChunkAsync com batching fixo ao inves deste metodo
    var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(buffer, options);
    await using var ms = new MemoryStream();
    await using (var brotli = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen: true))
    {
        await brotli.WriteAsync(jsonBytes);
    }
    return ms.Length >= targetCompressedBytes && buffer.Count > 0;
}
*/

static Task<byte[]> SerializeChunkPayloadAsync(UnifiedDataRecord data, ChunkMeta meta, JsonSerializerOptions options)
{
    var result = new
    {
        chunkId = (int)((meta.Start - 1) / meta.Count) + 1,
        data = new List<TombamentoRecord>(meta.Count),
        hash = meta.Hash
    };

    var allItems = data.Tabelas?.Tombamentos?.ToList();

    if (allItems is null || allItems.Count == 0)
    {
        Console.WriteLine($"[AVISO] SerializeChunkPayload: Nenhum tombamento encontrado");
        return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, options));
    }

    // Aplica filtro de exercício fiscal corrente
    var exercicioCorrente = DateTime.Now.Year;
    var itemsFiltrados = allItems
        .Where(p => p.ExercicioFiscal == exercicioCorrente || p.ExercicioFiscal == 0)
        .ToList();

    var start = meta.Start;
    var end = meta.Start + meta.Count - 1;

    // Extrai apenas os itens do chunk especificado
    for (int i = start - 1; i < end && i < itemsFiltrados.Count; i++)
    {
        var item = itemsFiltrados[i];
        if (item is not null)
            result.data.Add(item);
    }

    Console.WriteLine($"[INFO] SerializeChunkPayload: Chunk {result.chunkId} com {result.data.Count} registros");

    return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, options));
}

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
