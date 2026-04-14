# Padrão: Streaming NDJSON com Minimal API + Blazor

Use quando a API precisa permanecer no caminho (autenticação complexa, transformação de dados,
auditoria de acesso, dados não podem ser expostos via presigned URL).

## O que é NDJSON

Newline-Delimited JSON: cada linha é um objeto JSON independente.
```
{"id":"001","nome":"Cadeira","valor":350.00}
{"id":"002","nome":"Mesa","valor":1200.00}
{"id":"003","nome":"Computador","valor":4500.00}
```
Permite processar e exibir itens conforme chegam, sem esperar o arquivo inteiro.

## Backend — Minimal API com Streaming

```csharp
// InventarioStreamEndpoints.cs
public static class InventarioStreamEndpoints
{
    public static IEndpointRouteBuilder MapInventarioStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventario/{orgao}/stream", StreamInventario)
           .RequireAuthorization()
           .WithName("StreamInventario");

        return app;
    }

    private static async Task StreamInventario(
        string orgao,
        ClaimsPrincipal user,
        IInventarioS3Reader reader,
        IPermissaoService permissao,
        HttpContext ctx,
        CancellationToken ct)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await permissao.UsuarioTemAcessoAsync(userId, orgao, ct))
        {
            ctx.Response.StatusCode = 403;
            return;
        }

        ctx.Response.ContentType = "application/x-ndjson";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Desabilita buffering de resposta — bytes saem imediatamente
        var bufferingFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        await foreach (var bem in reader.StreamAsync(orgao, ct))
        {
            var linha = JsonSerializer.Serialize(bem) + "\n";
            await ctx.Response.WriteAsync(linha, ct);
            await ctx.Response.Body.FlushAsync(ct); // garante envio imediato
        }
    }
}
```

```csharp
// InventarioS3Reader.cs
public class InventarioS3Reader : IInventarioS3Reader
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public InventarioS3Reader(IAmazonS3 s3, IConfiguration config)
    {
        _s3 = s3;
        _bucket = config["S3:InventarioBucket"]!;
    }

    public async IAsyncEnumerable<BemPatrimonial> StreamAsync(
        string orgao,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucket,
            Key = $"inventario/{orgao}/dados.json"
        };

        using var response = await _s3.GetObjectAsync(request, ct);
        await using var stream = response.ResponseStream;

        // JsonSerializer.DeserializeAsyncEnumerable processa item a item
        // sem carregar o array inteiro em memória
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        await foreach (var bem in JsonSerializer.DeserializeAsyncEnumerable<BemPatrimonial>(
            stream, options, ct))
        {
            if (bem is not null) yield return bem;
        }
    }
}
```

**Importante:** `DeserializeAsyncEnumerable` requer que o JSON raiz seja um array `[...]`.

## Configuração de Timeout para Streaming

```csharp
// Program.cs
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
    // Não definir MaxRequestBodySize para endpoints de stream de saída
});
```

## Frontend — Blazor lendo NDJSON progressivamente

```csharp
// InventarioStreamService.cs
public class InventarioStreamService
{
    private readonly HttpClient _httpClient;

    public InventarioStreamService(IHttpClientFactory factory)
        => _httpClient = factory.CreateClient("Api");

    public async IAsyncEnumerable<BemPatrimonial> StreamOrgaoAsync(
        string orgao,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(
            $"/api/inventario/{orgao}/stream",
            HttpCompletionOption.ResponseHeadersRead, // não espera o body inteiro
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? linha;
        while ((linha = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;

            var bem = JsonSerializer.Deserialize<BemPatrimonial>(linha);
            if (bem is not null) yield return bem;
        }
    }
}
```

```razor
@* InventarioStreamViewer.razor *@
@inject InventarioStreamService StreamService
@implements IDisposable

<div>
    <p>@_bens.Count bens carregados @(_carregando ? "..." : "(concluído)")</p>
    <TabelaBens Bens="_bens" />
</div>

@code {
    [Parameter] public string Orgao { get; set; } = "";

    private List<BemPatrimonial> _bens = [];
    private bool _carregando;
    private CancellationTokenSource _cts = new();

    protected override async Task OnParametersSetAsync()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _bens.Clear();
        _carregando = true;

        try
        {
            await foreach (var bem in StreamService.StreamOrgaoAsync(Orgao, _cts.Token))
            {
                _bens.Add(bem);

                // Atualiza UI a cada 50 itens para não chamar StateHasChanged 4000x
                if (_bens.Count % 50 == 0)
                    StateHasChanged();
            }
        }
        catch (OperationCanceledException) { /* navegou para outra página */ }
        finally
        {
            _carregando = false;
            StateHasChanged();
        }
    }

    public void Dispose() => _cts.Cancel();
}
```

## Quando usar Presigned URL vs Streaming

| Critério | Presigned URL | NDJSON Streaming |
|---|---|---|
| Segurança (arquivo não pode ser exposto) | ❌ | ✅ |
| Simplicidade de implementação | ✅ | ❌ |
| Transformação dos dados na API | ❌ | ✅ |
| Log de acesso por campo | ❌ | ✅ |
| Performance máxima | ✅ | Boa |
| Funciona com CDN na frente da API | Parcial | ✅ |
