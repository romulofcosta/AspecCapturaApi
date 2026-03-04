using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    c.SwaggerDoc("v1", new() { Title = "ASPEC Capture API", Version = "v1" });
});

// ─── AWS ──────────────────────────────────────────────────────────────────────
var awsOptions = builder.Configuration.GetAWSOptions();
var accessKey = builder.Configuration["AWS:AccessKey"];
var secretKey = builder.Configuration["AWS:SecretKey"];

if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
{
    awsOptions.Credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);
}

builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonS3>();

// ─── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", corsBuilder =>
    {
        corsBuilder
            .SetIsOriginAllowed(origin =>
            {
                if (builder.Environment.IsDevelopment()) return true;
                return origin.EndsWith(".pwa-camera-poc-blazor.pages.dev");
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
    ReadCommentHandling = JsonCommentHandling.Skip
};

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddMemoryCache();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.MaxDepth = 0; // sem limite de profundidade
        options.JsonSerializerOptions.DefaultBufferSize = 16 * 1024 * 1024; // 16 MB, ajuste conforme necessário
    });


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

app.UseHttpsRedirection();
app.UseCors("AllowSpecificOrigins");

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


// ─── ENDPOINT: Login ──────────────────────────────────────────────────────────
app.MapPost("/api/auth/login", async (
    [FromBody] LoginRequest request,
    [FromServices] IAmazonS3 s3Client,
    [FromServices] IConfiguration configuration,
    ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Senha))
        return Results.BadRequest(new { error = "Usuário e Senha são obrigatórios." });

    var bucketName = configuration["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucketName))
        return Results.Problem("Bucket não configurado.");

    // Formato esperado: ce999.nome1.sbnome1
    var parts = request.Usuario.Split('.', 2);
    if (parts.Length < 2)
    {
        log.LogWarning("Formato de usuário inválido: {Usuario}", request.Usuario);
        return Results.BadRequest(new { error = "Formato de usuário inválido. Use: municipio.usuario" });
    }

    var prefix = parts[0].ToUpper();
    var s3Key = $"usuarios/{prefix}.json";

    log.LogInformation("Tentativa de login para município {Prefix}, usuário {Usuario}", prefix, request.Usuario);

    try
    {
        // 1. Carrega dados de usuário
        using var s3Response = await s3Client.GetObjectAsync(bucketName, s3Key);
        using var reader = new StreamReader(s3Response.ResponseStream);
        var json = await reader.ReadToEndAsync();
        var root = JsonSerializer.Deserialize<UnifiedDataRecord>(json, jsonOptions);
        if (root?.Usuarios == null || root.Usuarios.Count == 0)
        {
            log.LogWarning("Arquivo {S3Key} não contém usuários válidos.", s3Key);
            return Results.Unauthorized();
        }
        var restOfUsername = parts[1].ToLower();
        var user = root.Usuarios.FirstOrDefault(u =>
            string.Equals(u.NmUsuario, request.Usuario, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.NmUsuario, restOfUsername, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            log.LogWarning("Usuário '{Usuario}' não encontrado.", request.Usuario);
            return Results.Unauthorized();
        }
        if (user.PwdUsuario != request.Senha)
        {
            log.LogWarning("Senha inválida para usuário '{Usuario}'.", request.Usuario);
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

        var tombamentoFiltrado = user.Esfera == "A"
            ? tombamentoBase
            : tombamentoBase.Where(p => p.Esfera == user.Esfera).ToList();

        log.LogInformation("Tombamentos filtrados para esfera {Esfera}: {Count}", user.Esfera, tombamentoFiltrado.Count);

        // 5. Filtra patrimônio para árvore de órgãos (dados de apoio)
        var orgaos = BuildOrgaoHierarchy(user.Esfera, tombamentoFiltrado, tabelas);

        var tombamentos = tombamentoFiltrado.Select(p => new TombamentoItemResponse(p.IdPatomb, p.Nutomb, p.Esfera, p.Deprod)).AsQueryable();

        return Results.Stream(async stream =>
        {
            await JsonSerializer.SerializeAsync(stream, new AuthResponse(
                NomeCompleto: user.NomeCompleto ?? user.NmUsuario,
                Prefixo: prefix,
                Esfera: user.Esfera,
                Orgaos: orgaos,
                Tombamentos: tombamentos.ToList(),
                Token: Guid.NewGuid().ToString()
            ));
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
        
        var bucket = config["AWS:BucketName"];
        if (string.IsNullOrEmpty(bucket))
        {
            log.LogError("SyncInfo: Bucket não configurado");
            return Results.Problem("Bucket não configurado.");
        }
        
        var key = $"usuarios/{prefix.ToUpper()}.json";
        log.LogInformation("SyncInfo: Processando key={Key}", key);
        
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
        
        var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache);
        
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
        
        var bucket = config["AWS:BucketName"];
        if (string.IsNullOrEmpty(bucket))
        {
            log.LogError("Lote: Bucket não configurado");
            return Results.Problem("Bucket não configurado.");
        }
        
        var key = $"usuarios/{prefix.ToUpper()}.json";
        log.LogInformation("Lote: Processando chunk {ChunkId} para key={Key}", id, key);
        
        var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache);
        
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
            payload = await SerializeChunkPayloadAsync(s3, bucket, key, chunkMeta, index.Options);
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

app.Run();

// ─── Funções auxiliares ───────────────────────────────────────────────────────
static List<OrgaoRecord> BuildOrgaoHierarchy(
    string esfera,
    List<TombamentoRecord> filteredTombamento,
    TabelasRecord tabelas)
{
    var orgaos = new List<OrgaoRecord>();

    var targetOrgaoCodes = esfera == "A"
        ? tabelas.XxOrga.Select(o => o.CdOrgao).Distinct()
        : filteredTombamento.Select(p => p.CdOrgao).Distinct();

    foreach (var orgCode in targetOrgaoCodes)
    {
        var orgInfo = tabelas.XxOrga.FirstOrDefault(o => o.CdOrgao == orgCode);
        if (orgInfo is null) continue;

        var targetUoCodes = esfera == "A"
            ? tabelas.XxUnid.Where(u => u.CdOrgao == orgCode).Select(u => u.CdUnid).Distinct()
            : filteredTombamento.Where(p => p.CdOrgao == orgCode).Select(p => p.CdUnid).Distinct();

        var uos = new List<UnidadeOrcamentariaRecord>();
        foreach (var uoCode in targetUoCodes)
        {
            var uoInfo = tabelas.XxUnid.FirstOrDefault(u => u.CdOrgao == orgCode && u.CdUnid == uoCode);
            if (uoInfo is null) continue;

            var locs = tabelas.Localizacao
                .Where(l => l.CdOrgao == orgCode && l.CdUnid == uoCode)
                .ToList();

            var targetAreaCodes = esfera == "A"
                ? locs.Select(l => l.CdArea).Distinct()
                : filteredTombamento
                    .Where(p => p.CdOrgao == orgCode && p.CdUnid == uoCode)
                    .Select(p => p.CdArea).Distinct();

            var areas = new List<AreaRecord>();
            foreach (var areaCode in targetAreaCodes)
            {
                var areaInfo = tabelas.PaArea.FirstOrDefault(a => a.CdArea == areaCode);
                if (areaInfo is null) continue;

                var targetSubCodes = esfera == "A"
                    ? locs.Where(l => l.CdArea == areaCode).Select(l => l.CdSArea).Distinct()
                    : filteredTombamento
                        .Where(p => p.CdOrgao == orgCode && p.CdUnid == uoCode && p.CdArea == areaCode)
                        .Select(p => p.CdSArea).Distinct();

                var subareas = new List<SubareaRecord>();
                foreach (var subCode in targetSubCodes)
                {
                    var subInfo = tabelas.PasArea.FirstOrDefault(s => s.CdSArea == subCode);
                    if (subInfo is null) continue;
                    subareas.Add(new SubareaRecord(subCode, subInfo.NmSArea));
                }

                areas.Add(new AreaRecord(areaCode, areaInfo.NmArea, subareas));
            }

            uos.Add(new UnidadeOrcamentariaRecord(uoCode, uoInfo.NmUnid, areas));
        }

        orgaos.Add(new OrgaoRecord(orgCode, orgInfo.NmOrgao, uos));
    }

    return orgaos;
}

static async Task<ChunkIndex> BuildOrGetChunkIndexAsync(IAmazonS3 s3, string bucket, string key, IMemoryCache cache)
{
    var head = await s3.GetObjectMetadataAsync(bucket, key);
    var dt = head.LastModified ?? DateTime.UtcNow;
    var version = dt.ToUniversalTime().ToString("yyyyMMddHHmmss");
    var cacheKey = $"TOMB-INDEX:{bucket}:{key}:{version}";
    if (cache.TryGetValue<ChunkIndex>(cacheKey, out var cached) && cached is not null) 
        return cached;
    
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
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ESTRATEGIA 1: BATCHING FIXO (RECOMENDADA)
    // Performance: O(n) - ~10-20 segundos para 42 MB
    // ═══════════════════════════════════════════════════════════════════════════
    const int MaxItemsPerChunk = 5000; // Ajuste conforme necessario (5k-10k recomendado)
    
    using var obj = await s3.GetObjectAsync(bucket, key);
    using var stream = obj.ResponseStream;
    
    // ⚠️ CORRECAO: Usar await ao inves de .Result e validar null
    var unifiedData = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, options);
    var itens = unifiedData?.Tabelas?.Tombamentos?.ToList();
    
    // Validacao critica: se itens for null, retorna index vazio
    if (itens is null || itens.Count == 0)
    {
        Console.WriteLine($"[AVISO] Nenhum tombamento encontrado em {key}");
        var emptyIndex = new ChunkIndex(version, 0, "", new List<ChunkMeta>(), options);
        cache.Set(cacheKey, emptyIndex, TimeSpan.FromHours(1));
        return emptyIndex;
    }

    Console.WriteLine($"[INFO] Processando {itens.Count} tombamentos de {key}");

    foreach (var item in itens)
    {
        if (item is null) continue;
        current.Add(item);
        total++;
        
        // Verifica apenas o tamanho do buffer, nao serializa/comprime a cada item
        if (current.Count >= MaxItemsPerChunk)
        {
            await FinalizeChunkAsync(current, total, sha256, globalHashCtx, chunks, options);
            current.Clear();
        }
    }
    
    // Processa o ultimo chunk (se houver registros restantes)
    if (current.Count > 0)
    {
        await FinalizeChunkAsync(current, total, sha256, globalHashCtx, chunks, options);
    }
    
    var globalHash = Convert.ToHexString(globalHashCtx.GetHashAndReset());
    var index = new ChunkIndex(version, total, globalHash, chunks, options);
    cache.Set(cacheKey, index, TimeSpan.FromHours(1));
    return index;
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

static async Task<byte[]> SerializeChunkPayloadAsync(IAmazonS3 s3, string bucket, string key, ChunkMeta meta, JsonSerializerOptions options)
{
    var result = new
    {
        chunkId = (int)((meta.Start - 1) / meta.Count) + 1,
        data = new List<TombamentoRecord>(meta.Count),
        hash = meta.Hash
    };
    
    using var obj = await s3.GetObjectAsync(bucket, key);
    using var stream = obj.ResponseStream;
    
    // ⚠️ CORRECAO: Usar UnifiedDataRecord ao inves de DeserializeAsyncEnumerable
    var unifiedData = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, options);
    var allItems = unifiedData?.Tabelas?.Tombamentos?.ToList();
    
    if (allItems is null || allItems.Count == 0)
    {
        Console.WriteLine($"[AVISO] SerializeChunkPayload: Nenhum tombamento encontrado em {key}");
        return JsonSerializer.SerializeToUtf8Bytes(result, options);
    }
    
    var start = meta.Start;
    var end = meta.Start + meta.Count - 1;
    
    // Extrai apenas os itens do chunk especificado
    for (int i = start - 1; i < end && i < allItems.Count; i++)
    {
        var item = allItems[i];
        if (item is not null)
        {
            result.data.Add(item);
        }
    }
    
    Console.WriteLine($"[INFO] SerializeChunkPayload: Chunk {result.chunkId} com {result.data.Count} registros");
    
    return JsonSerializer.SerializeToUtf8Bytes(result, options);
}

// ─── DTOs e Records ───────────────────────────────────────────────────────────
public record PresignedUrlRequest(string FileName, string ContentType, string AssetId, string AssetCode);
public record PresignedUrlResponse(string Url, string Key);
public record LoginRequest(string Usuario, string Senha);

public record AuthResponse(
    string NomeCompleto,
    string Prefixo,
    string Esfera,
    List<OrgaoRecord> Orgaos,
    List<TombamentoItemResponse> Tombamentos,
    string Token
);

public record TombamentoItemResponse(long IdPatomb, string Nutomb, string Esfera, string Deprod);

public class TombamentoCargaItem
{
    [JsonPropertyName("idpatomb")]
    public long IdPatomb { get; set; }
    [JsonPropertyName("nutomb")]
    public string Nutomb { get; set; } = string.Empty;
    [JsonPropertyName("deprod")]
    public string Deprod { get; set; } = string.Empty;
    [JsonPropertyName("esfera")]
    public string Esfera { get; set; } = string.Empty;
    [JsonPropertyName("cdorgao")]
    public string CdOrgao { get; set; } = string.Empty;
    [JsonPropertyName("cdunid")]
    public string CdUnid { get; set; } = string.Empty;
    [JsonPropertyName("cdarea")]
    public string CdArea { get; set; } = string.Empty;
    [JsonPropertyName("cdsarea")]
    public string CdSArea { get; set; } = string.Empty;
}

// ─── Records para o JSON Unificado ───────────────────────────────────────────
// CORREÇÃO BUG #1: [JsonPropertyName] garante que a desserialização case-insensitive
// funcione corretamente com records posicionais em .NET

public record UnifiedDataRecord(
    [property: JsonPropertyName("cliente")] string? Cliente,
    [property: JsonPropertyName("usuarios")] List<UserRecord>? Usuarios,
    [property: JsonPropertyName("tabelas")] TabelasRecord? Tabelas
);

// CORREÇÃO BUG #2: NomeCompleto é nullable pois pode não existir no JSON
public record UserRecord(
    [property: JsonPropertyName("idusuario")] string IdUsuario,
    [property: JsonPropertyName("nmusuario")] string NmUsuario,
    [property: JsonPropertyName("pwdusuario")] string PwdUsuario,
    [property: JsonPropertyName("esfera")] string Esfera,
    [property: JsonPropertyName("nomecompleto")] string? NomeCompleto
);

public record TabelasRecord(
    [property: JsonPropertyName("xxorga")] List<XxOrgaRecord> XxOrga,
    [property: JsonPropertyName("xxunid")] List<XxUnidRecord> XxUnid,
    [property: JsonPropertyName("paarea")] List<PaAreaRecord> PaArea,
    [property: JsonPropertyName("pasarea")] List<PasAreaRecord> PasArea,
    [property: JsonPropertyName("localizacao")] List<LocalizacaoRecord> Localizacao,
    [property: JsonPropertyName("tombamentos")] List<TombamentoRecord> Tombamentos
);

public record XxOrgaRecord(
    [property: JsonPropertyName("cdorgao")] string CdOrgao,
    [property: JsonPropertyName("nmorgao")] string NmOrgao
);

public record XxUnidRecord(
    [property: JsonPropertyName("cdorgao")] string CdOrgao,
    [property: JsonPropertyName("cdunid")] string CdUnid,
    [property: JsonPropertyName("nmunid")] string NmUnid
);

public record PaAreaRecord(
    [property: JsonPropertyName("cdarea")] string CdArea,
    [property: JsonPropertyName("nmarea")] string NmArea
);

public record PasAreaRecord(
    [property: JsonPropertyName("cdsarea")] string CdSArea,
    [property: JsonPropertyName("nmsarea")] string NmSArea
);

public record LocalizacaoRecord(
    [property: JsonPropertyName("cdorgao")] string CdOrgao,
    [property: JsonPropertyName("cdunid")] string CdUnid,
    [property: JsonPropertyName("cdarea")] string CdArea,
    [property: JsonPropertyName("cdsarea")] string CdSArea
);

public record TombamentoRecord(
    [property: JsonPropertyName("idpatomb")] long IdPatomb,
    [property: JsonPropertyName("nutomb")] string Nutomb,
    [property: JsonPropertyName("deprod")] string Deprod,
    [property: JsonPropertyName("esfera")] string Esfera,
    [property: JsonPropertyName("cdorgao")] string CdOrgao,
    [property: JsonPropertyName("cdunid")] string CdUnid,
    [property: JsonPropertyName("cdarea")] string CdArea,
    [property: JsonPropertyName("cdsarea")] string CdSArea
);

// ─── Records de resposta hierárquica ─────────────────────────────────────────
public record OrgaoRecord(string IdOrgao, string NomeOrgao, List<UnidadeOrcamentariaRecord> UnidadesOrcamentarias);
public record UnidadeOrcamentariaRecord(string IdUO, string NomeUO, List<AreaRecord> Areas);
public record AreaRecord(string IdArea, string NomeArea, List<SubareaRecord> Subareas);
public record SubareaRecord(string IdSubarea, string NomeSubarea);

record ChunkMeta(int Start, int Count, string Hash);
record ChunkIndex(string Version, int TotalRecords, string GlobalHash, List<ChunkMeta> Chunks, JsonSerializerOptions Options);
