using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AWS Configuration
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonS3>();

// CORS Configuration
// Define allowed origins for the Blazor PWA frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
        corsBuilder =>
        {
            // Production origin (Cloudflare Pages deployment)
            var allowedOrigins = new List<string>
            {
                "https://pwa-camera-poc-blazor.pages.dev"
            };

            // Add localhost origins for development
            if (builder.Environment.IsDevelopment())
            {
                allowedOrigins.Add("https://localhost:5001");
                allowedOrigins.Add("http://localhost:5000");
                allowedOrigins.Add("https://localhost:7001");
                allowedOrigins.Add("http://localhost:7000");
                allowedOrigins.Add("http://localhost:5230");
                allowedOrigins.Add("https://localhost:5231");
            }

            corsBuilder.WithOrigins(allowedOrigins.ToArray())
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

app.MapPost("/api/storage/presigned-url", async ([FromBody] PresignedUrlRequest request, [FromServices] IAmazonS3 s3Client, [FromServices] IConfiguration configuration) =>
{
    var bucketName = configuration["AWS:BucketName"];
    if (string.IsNullOrEmpty(bucketName))
    {
        return Results.Problem("AWS BucketName not configured.");
    }

    // Use AssetId as folder if provided, otherwise 'temp'
    var folder = !string.IsNullOrEmpty(request.AssetId) ? request.AssetId : "temp";
    var key = $"{folder}/{request.FileName}";

    var expiryDuration = TimeSpan.FromMinutes(15);

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

app.Run();

public record PresignedUrlRequest(string FileName, string ContentType, string AssetId, string AssetCode);
public record PresignedUrlResponse(string Url, string Key);