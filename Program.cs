using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AWS Configuration
var awsOptions = builder.Configuration.GetAWSOptions();
var accessKey = builder.Configuration["AWS:AccessKey"];
var secretKey = builder.Configuration["AWS:SecretKey"];

if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
{
    awsOptions.Credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);
}

builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonS3>();

// CORS Configuration
// Define allowed origins for the Blazor PWA frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
        corsBuilder =>
        {
            corsBuilder.SetIsOriginAllowed(origin =>
                       {
                           // Allow any origin in Development to fix local PWA access
                           if (builder.Environment.IsDevelopment()) return true;

                           // Production origins
                           return origin == "https://pwa-camera-poc-blazor.pages.dev";
                       })
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials(); // Important for authentication cookies/tokens
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowSpecificOrigins");

// Redirect root to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"));

// Auto-configure S3 CORS for Browser Direct Uploads
try
{
    using (var scope = app.Services.CreateScope())
    {
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var bucketName = config["AWS:BucketName"];
        var region = config["AWS:Region"];

        // Only attempt if not using placeholders
        if (!string.IsNullOrEmpty(bucketName) && bucketName != "AWS__BucketName" &&
            !string.IsNullOrEmpty(region) && region != "AWS__Region")
        {
            var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
            await s3.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
            {
                BucketName = bucketName,
                Configuration = new CORSConfiguration
                {
                    Rules = new List<CORSRule>
                    {
                        new CORSRule
                        {
                            AllowedMethods = new List<string> { "GET", "PUT", "POST", "HEAD", "DELETE" },
                            AllowedOrigins = new List<string> { "*" }, // For Dev/PoC only. In Prod restrict to domain.
                            AllowedHeaders = new List<string> { "*" }, // Allows Content-Type and others
                            ExposeHeaders = new List<string> { "ETag", "x-amz-meta-asset-code" },
                            MaxAgeSeconds = 3000
                        }
                    }
                }
            });
            Console.WriteLine($"✅ S3 CORS Configured for bucket {bucketName}");
        }
        else
        {
            Console.WriteLine("⚠️ AWS Configuration not fully set (placeholders detected). Skipping S3 CORS auto-config.");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ Failed to configure S3 CORS or AWS service: {ex.Message}");
}

app.MapPost("/api/storage/presigned-url", async ([FromBody] PresignedUrlRequest request, [FromServices] IAmazonS3 s3Client, [FromServices] IConfiguration configuration) =>
{
    var bucketName = configuration["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucketName))
    {
        return Results.Problem("AWS BucketName not configured.");
    }

    // Use AssetId as folder if provided, otherwise 'temp'
    // Decode first in case the client sent encoded data
    var rawFolder = !string.IsNullOrEmpty(request.AssetId) ? Uri.UnescapeDataString(request.AssetId) : "temp";

    // Helper function to sanitize keys (remove accents, spaces to hyphens)
    string SanitizeKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Normalize to FormD to split accents
        var normalizedString = input.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC)
                            .Replace(" ", "-") // Spaces to hyphens
                            .Replace("%20", "-") // Encoded spaces to hyphens
                            .Replace("/", "-") // Slashes to hyphens
                            .Replace("\\", "-");
    }

    // Split folder by '/' to sanitize each segment if it's a path
    var folderSegments = rawFolder.Split('/');
    var sanitizedFolder = string.Join("/", folderSegments.Select(s => SanitizeKey(s)));
    var sanitizedFileName = SanitizeKey(request.FileName);

    var key = $"{sanitizedFolder}/{sanitizedFileName}";

    Console.WriteLine($"DEBUG: Generated Key for Pre-signed URL: {key}");

    var expiryDuration = TimeSpan.FromMinutes(10);

    var presignedUrlRequest = new GetPreSignedUrlRequest
    {
        BucketName = bucketName,
        Key = key,
        Verb = HttpVerb.PUT,
        Expires = DateTime.UtcNow.Add(expiryDuration),
        ContentType = request.ContentType
    };

    // Add metadata if needed
    if (!string.IsNullOrEmpty(request.AssetCode))
    {
        Console.WriteLine($"DEBUG: Signing with asset-code: {request.AssetCode}");
        presignedUrlRequest.Metadata.Add("asset-code", request.AssetCode);
    }

    string url = "";
    try
    {
        url = s3Client.GetPreSignedURL(presignedUrlRequest);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error generating URL: {ex.Message}");
    }

    return Results.Ok(new PresignedUrlResponse(url, key));
})
.WithName("GetPresignedUrl")
.WithOpenApi();

app.MapGet("/api/storage/exists/{*filePath}", async (string filePath, [FromServices] IAmazonS3 s3Client, [FromServices] IConfiguration configuration) =>
{
    var bucketName = configuration["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucketName)) return Results.Problem("Bucket not configured");

    try
    {
        // Check if the object exists by fetching metadata
        // {*filePath} is a catch-all that correctly handles slashes in the S3 key
        await s3Client.GetObjectMetadataAsync(bucketName, filePath);

        var region = configuration["AWS:Region"] ?? "us-east-1";
        // Ensure the URL is properly constructed
        var url = $"https://{bucketName}.s3.{region}.amazonaws.com/{filePath}";

        return Results.Ok(new { exists = true, key = filePath, url = url });
    }
    catch (Amazon.S3.AmazonS3Exception ex)
    {
        if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Ok(new { exists = false, key = filePath });
        }
        // Return 404 for Forbidden as well, often implies object not found or no access
        if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Log the error but return "not found" or specific error to help debug
            Console.WriteLine($"S3 Forbidden for key '{filePath}': {ex.Message}");
            return Results.Problem($"Access Denied (Forbidden) for key: {filePath}. Ensure S3 permissions.", statusCode: 403);
        }
        return Results.Problem($"S3 Error: {ex.StatusCode} - {ex.Message}");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("CheckObjectExists")
.WithOpenApi();

app.MapPost("/api/auth/login", async ([FromBody] LoginRequest request, [FromServices] IAmazonS3 s3Client, [FromServices] IConfiguration configuration) =>
{
    if (string.IsNullOrEmpty(request.Usuario) || string.IsNullOrEmpty(request.Senha))
    {
        return Results.BadRequest("Usuário e Senha são obrigatórios.");
    }

    var bucketName = configuration["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucketName)) return Results.Problem("Bucket not configured");

    // Parsing: CE305.joao.silva -> Prefix = CE305, Remaining = joao.silva
    var parts = request.Usuario.Split('.', 2);
    if (parts.Length < 2)
    {
        return Results.Unauthorized(); // Formato inválido
    }

    var prefix = parts[0].ToUpper();
    var restOfUsername = parts[1].ToLower();
    var s3Key = $"usuarios/{prefix}.json";

    try
    {
        // Download user list from S3
        using var responseMessage = await s3Client.GetObjectAsync(bucketName, s3Key);
        using var reader = new StreamReader(responseMessage.ResponseStream);
        var json = await reader.ReadToEndAsync();

        var users = System.Text.Json.JsonSerializer.Deserialize<List<UsuarioRecord>>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (users == null) return Results.Unauthorized();

        // Validação do usuário
        var user = users.FirstOrDefault(u => u.Usuario.ToLower() == restOfUsername);
        if (user == null || user.Senha != request.Senha)
        {
            return Results.Unauthorized();
        }

        // Resposta de sucesso com estrutura hierárquica completa
        return Results.Ok(new AuthResponse(
            NomeCompleto: user.NomeCompleto,
            Prefixo: prefix,
            Orgaos: user.Orgaos,
            Token: Guid.NewGuid().ToString()
        ));
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound($"Configuração do município '{prefix}' não encontrada.");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Erro de autenticação: {ex.Message}");
    }
})
.WithName("Login")
.WithOpenApi();

app.Run();

public record PresignedUrlRequest(string FileName, string ContentType, string AssetId, string AssetCode);
public record PresignedUrlResponse(string Url, string Key);
public record LoginRequest(string Usuario, string Senha);
public record AuthResponse(string NomeCompleto, string Prefixo, List<OrgaoRecord> Orgaos, string Token);
public record UsuarioRecord(string Usuario, string Senha, string NomeCompleto, List<OrgaoRecord> Orgaos);
public record OrgaoRecord(string IdOrgao, string NomeOrgao, List<UnidadeOrcamentariaRecord> UnidadesOrcamentarias);
public record UnidadeOrcamentariaRecord(string IdUO, string NomeUO, List<AreaRecord> Areas);
public record AreaRecord(string IdArea, string NomeArea, List<SubareaRecord> Subareas);
public record SubareaRecord(string IdSubarea, string NomeSubarea);

