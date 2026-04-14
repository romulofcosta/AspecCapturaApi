using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using AspecCapturaApi.Models;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace AspecCapturaApi.Tests;

/// <summary>
/// Shared WebApplicationFactory with a mocked IAmazonS3 for capture endpoint tests.
/// </summary>
public class CaptureTestFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "test-secret-key-for-unit-tests-only-32chars!";

    public Mock<IAmazonS3> S3Mock { get; } = new Mock<IAmazonS3>();

    // The in-memory JSON document that S3 mock will serve/store
    public UnifiedDataRecord DataRoot { get; set; } = BuildDefaultRoot();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real S3 and replace with mock
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAmazonS3));
            if (descriptor != null) services.Remove(descriptor);
            services.AddSingleton(S3Mock.Object);

            // Provide minimal AWS config so the app doesn't crash
            services.Configure<Microsoft.Extensions.Options.IOptions<object>>(_ => { });
        });

        builder.UseSetting("AWS:BucketName", "test-bucket");
        builder.UseSetting("AWS:Region", "us-east-1");
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
        builder.UseSetting("Security:JwtSecret", "test-secret-key-for-unit-tests-only-32chars!");
        builder.UseSetting("Security:JwtIssuer", "aspec-capture-api");
        builder.UseSetting("Security:JwtAudience", "aspec-capture-client");

        // Garante que as variáveis de ambiente necessárias estejam disponíveis para o Program.cs
        Environment.SetEnvironmentVariable("JWT_SECRET", "test-secret-key-for-unit-tests-only-32chars!");
        Environment.SetEnvironmentVariable("JWT_ISSUER", "aspec-capture-api");
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", "aspec-capture-client");
        Environment.SetEnvironmentVariable("AWS_BUCKET_NAME", "test-bucket");
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");

        SetupS3Mock();
    }

    /// <summary>
    /// Cria um HttpClient com JWT válido no header Authorization para testes de endpoints protegidos.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string username = "ce999.admin", string prefix = "CE999", string esfera = "E")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "aspec-capture-api",
            audience: "aspec-capture-client",
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Name, username),
                new Claim("prefix", prefix),
                new Claim("esfera", esfera)
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenString);
        return client;
    }

    public void SetupS3Mock()    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // GetObjectMetadataAsync — always succeeds for CE999
        S3Mock.Setup(s => s.GetObjectMetadataAsync(
                "test-bucket",
                It.Is<string>(k => k.Contains("CE999")),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse { LastModified = DateTime.UtcNow });

        // GetObjectAsync — returns current DataRoot serialized (sobrecarga com string bucket/key)
        S3Mock.Setup(s => s.GetObjectAsync(
                "test-bucket",
                It.Is<string>(k => k.Contains("CE999")),
                default))
            .ReturnsAsync(() =>
            {
                var json = JsonSerializer.Serialize(DataRoot, options);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var response = new GetObjectResponse();
                response.ResponseStream = stream;
                return response;
            });

        // GetObjectAsync — returns current DataRoot serialized (sobrecarga com GetObjectRequest)
        S3Mock.Setup(s => s.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.BucketName == "test-bucket" && r.Key.Contains("CE999")),
                default))
            .ReturnsAsync(() =>
            {
                var json = JsonSerializer.Serialize(DataRoot, options);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var response = new GetObjectResponse();
                response.ResponseStream = stream;
                return response;
            });

        // PutObjectAsync — captures the written JSON back into DataRoot
        S3Mock.Setup(s => s.PutObjectAsync(
                It.Is<PutObjectRequest>(r => r.BucketName == "test-bucket"),
                default))
            .ReturnsAsync((PutObjectRequest req, CancellationToken _) =>
            {
                using var reader = new StreamReader(req.InputStream);
                var json = reader.ReadToEnd();
                DataRoot = JsonSerializer.Deserialize<UnifiedDataRecord>(json, options)!;
                return new PutObjectResponse();
            });

        // GetObjectMetadataAsync — 404 for unknown prefixes
        S3Mock.Setup(s => s.GetObjectMetadataAsync(
                "test-bucket",
                It.Is<string>(k => !k.Contains("CE999")),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not Found")
            {
                StatusCode = System.Net.HttpStatusCode.NotFound
            });

        // GetObjectAsync — 404 for unknown prefixes (sobrecarga com string bucket/key)
        S3Mock.Setup(s => s.GetObjectAsync(
                "test-bucket",
                It.Is<string>(k => !k.Contains("CE999")),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not Found")
            {
                StatusCode = System.Net.HttpStatusCode.NotFound
            });

        // GetObjectAsync — 404 for unknown prefixes (sobrecarga com GetObjectRequest)
        S3Mock.Setup(s => s.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.BucketName == "test-bucket" && !r.Key.Contains("CE999")),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not Found")
            {
                StatusCode = System.Net.HttpStatusCode.NotFound
            });
    }

    public static UnifiedDataRecord BuildDefaultRoot() => new UnifiedDataRecord(
        Cliente: "ce999",
        Usuarios: new List<UserRecord>
        {
            new UserRecord("u1", "ce999.admin", "senha123", "E", "Admin Teste")
        },
        Tabelas: new TabelasRecord(
            XxOrga: new List<XxOrgaRecord>(),
            XxUnid: new List<XxUnidRecord>(),
            PaArea: new List<PaAreaRecord>(),
            PasArea: new List<PasAreaRecord>(),
            Localizacao: new List<LocalizacaoRecord>(),
            Tombamentos: new List<TombamentoRecord>
            {
                new TombamentoRecord
                {
                    IdPatomb = 619859188188L,
                    Nutomb = "00000005",
                    Databomb = 20200615,
                    Cdprod = 65380,
                    Deprod = "ARMARIO 2 PORTAS",
                    Estado = "BOM",
                    Dataestado = 20200615,
                    Situacao = null,
                    Datasituacao = null,
                    IdLocalizacao = null,
                    Esfera = "E"
                }
            }
        )
    );
}
