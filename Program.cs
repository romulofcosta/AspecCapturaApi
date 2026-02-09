using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using pwa_camera_poc_api.Middleware;
using pwa_camera_poc_api.Models;

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

// v1.5.0: Register API Key Auth Middleware for /api/v2/* endpoints
app.UseMiddleware<ApiKeyAuthMiddleware>();

// Redirect root to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"));

// Auto-configure S3 CORS for Browser Direct Uploads
using (var scope = app.Services.CreateScope())
{
    var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var bucketName = config["AWS:BucketName"];

    if (!string.IsNullOrEmpty(bucketName))
    {
        try
        {
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
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to configure S3 CORS (Check permissions): {ex.Message}");
        }
    }
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

// v1.5.0 API v2 Endpoints - Provisioning
// GET /api/v2/inventario/carga/{ugId} - Returns Pre-Signed URL to inventory JSON
app.MapGet("/api/v2/inventario/carga/{ugId}", async (int ugId, [FromServices] IAmazonS3 s3Client, [FromServices] IConfiguration config, [FromServices] ILogger<Program> logger) =>
{
    try
    {
        var bucketName = config["AWS:BucketName"] ?? "aspec-capture";
        var s3CargasPath = config["AWS:S3Paths:Cargas"] ?? "cargas";
        var key = $"{s3CargasPath}/ug_{ugId}_itens.json";

        logger.LogInformation($"[v2 API] Generating Pre-Signed URL for inventory carga: {key}");

        var presignedUrlRequest = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(30)
        };

        var presignedUrl = s3Client.GetPreSignedURL(presignedUrlRequest);

        return Results.Ok(new ProvisioningUrlResponseDto
        {
            PresignedUrl = presignedUrl,
            Key = key,
            Bucket = bucketName,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            ContentType = "application/json"
        });
    }
    catch (Exception ex)
    {
        logger.LogError($"[v2 API] Error generating inventory URL: {ex.Message}");
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }
})
.WithName("GetInventorioCarga")
.WithOpenApi()
.Produces<ProvisioningUrlResponseDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/v2/auth/usuarios/{ugId} - Returns Pre-Signed URL to users JSON
app.MapGet("/api/v2/auth/usuarios/{ugId}", async (int ugId, [FromServices] IAmazonS3 s3Client, [FromServices] IConfiguration config, [FromServices] ILogger<Program> logger) =>
{
    try
    {
        var bucketName = config["AWS:BucketName"] ?? "aspec-capture";
        var s3CargasPath = config["AWS:S3Paths:Cargas"] ?? "cargas";
        var key = $"{s3CargasPath}/ug_{ugId}_users.json";

        logger.LogInformation($"[v2 API] Generating Pre-Signed URL for users: {key}");

        var presignedUrlRequest = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(30)
        };

        var presignedUrl = s3Client.GetPreSignedURL(presignedUrlRequest);

        return Results.Ok(new ProvisioningUrlResponseDto
        {
            PresignedUrl = presignedUrl,
            Key = key,
            Bucket = bucketName,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            ContentType = "application/json"
        });
    }
    catch (Exception ex)
    {
        logger.LogError($"[v2 API] Error generating users URL: {ex.Message}");
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }
})
.WithName("GetUsuarios")
.WithOpenApi()
.Produces<ProvisioningUrlResponseDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status500InternalServerError);

app.Run();

public record PresignedUrlRequest(string FileName, string ContentType, string AssetId, string AssetCode);
public record PresignedUrlResponse(string Url, string Key);