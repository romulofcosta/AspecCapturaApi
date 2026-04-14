# Padrão: Presigned URL S3 com Blazor

## Fluxo completo

```
1. Blazor chama GET /api/inventario/token?orgao=001
2. Minimal API valida JWT do usuário, verifica permissão para o órgão
3. API gera presigned URL com expiração de 15 minutos
4. Blazor usa a URL para buscar o JSON diretamente do S3
5. S3 responde com o arquivo (sem passar pela API)
```

## Backend — Minimal API

```csharp
// InventarioEndpoints.cs
public static class InventarioEndpoints
{
    public static IEndpointRouteBuilder MapInventarioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventario/token", GetPresignedUrl)
           .RequireAuthorization()
           .WithName("GetInventarioToken");

        return app;
    }

    private static async Task<IResult> GetPresignedUrl(
        string orgao,
        ClaimsPrincipal user,
        IS3PresignService presignService,
        IPermissaoService permissaoService,
        CancellationToken ct)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (!await permissaoService.UsuarioTemAcessoAsync(userId, orgao, ct))
            return Results.Forbid();

        var url = await presignService.GerarUrlAsync(
            bucket: "meu-bucket-inventario",
            key: $"inventario/{orgao}/dados.json",
            expiracao: TimeSpan.FromMinutes(15),
            ct: ct);

        return Results.Ok(new { url, expiraEm = DateTime.UtcNow.AddMinutes(15) });
    }
}
```

```csharp
// S3PresignService.cs
public class S3PresignService : IS3PresignService
{
    private readonly IAmazonS3 _s3;

    public S3PresignService(IAmazonS3 s3) => _s3 = s3;

    public Task<string> GerarUrlAsync(string bucket, string key, TimeSpan expiracao, CancellationToken ct)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiracao),
            Verb = HttpVerb.GET
        };

        // Síncrono — SDK v3 ainda não tem versão async para presign
        return Task.FromResult(_s3.GetPreSignedURL(request));
    }
}
```

## Frontend — Blazor

```csharp
// InventarioService.cs
public class InventarioService
{
    private readonly HttpClient _apiClient;
    private readonly HttpClient _s3Client; // sem auth headers

    public InventarioService(IHttpClientFactory factory)
    {
        _apiClient = factory.CreateClient("Api");
        _s3Client = factory.CreateClient("S3Direto");
    }

    public async Task<List<BemPatrimonial>> CarregarOrgaoAsync(
        string orgao, 
        CancellationToken ct = default)
    {
        // Passo 1: obter URL temporária
        var tokenResponse = await _apiClient.GetFromJsonAsync<TokenResponse>(
            $"/api/inventario/token?orgao={orgao}", ct);

        // Passo 2: buscar JSON direto no S3
        var response = await _s3Client.GetAsync(tokenResponse!.Url, ct);
        response.EnsureSuccessStatusCode();

        // Passo 3: deserializar via stream (não carrega tudo em memória)
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<List<BemPatrimonial>>(
            stream, 
            JsonSerializerOptions.Web, 
            ct) ?? [];
    }
}
```

```razor
@* InventarioViewer.razor *@
@inject InventarioService InventarioService

@if (_carregando)
{
    <LoadingIndicator Mensagem="Carregando inventário..." />
}
else if (_erro is not null)
{
    <ErroPanel Mensagem=@_erro OnRetry="CarregarAsync" />
}
else
{
    <TabelaBens Bens="_bens" />
}

@code {
    [Parameter] public string Orgao { get; set; } = "";

    private List<BemPatrimonial> _bens = [];
    private bool _carregando;
    private string? _erro;

    protected override async Task OnParametersSetAsync() => await CarregarAsync();

    private async Task CarregarAsync()
    {
        _carregando = true;
        _erro = null;
        try
        {
            _bens = await InventarioService.CarregarOrgaoAsync(Orgao);
        }
        catch (Exception ex)
        {
            _erro = $"Não foi possível carregar o inventário: {ex.Message}";
        }
        finally
        {
            _carregando = false;
        }
    }
}
```

## Configuração de CORS no S3

No bucket S3, configure CORS para aceitar requisições do domínio do Blazor:

```json
[
  {
    "AllowedHeaders": ["*"],
    "AllowedMethods": ["GET"],
    "AllowedOrigins": ["https://seu-app.dominio.gov.br"],
    "ExposeHeaders": ["Content-Length"],
    "MaxAgeSeconds": 900
  }
]
```

## Registro no Program.cs

```csharp
builder.Services.AddHttpClient("Api", c => c.BaseAddress = new Uri(builder.Configuration["ApiUrl"]!))
    .AddBearerToken(); // ou seu middleware de auth

builder.Services.AddHttpClient("S3Direto"); // sem headers de auth

builder.Services.AddScoped<IS3PresignService, S3PresignService>();
builder.Services.AddScoped<InventarioService>();
```
