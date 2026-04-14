---
name: aspnet-performance-diagnostic
description: >
  Use esta skill sempre que o usuário colar código de ASP.NET Core (Minimal API, Web API, ou qualquer
  middleware) e pedir análise de performance, diagnóstico de travamento, timeout, memory leak,
  lentidão, ou refatoração arquitetural. Também dispara para: análise de endpoints com problemas,
  revisão de uso de HttpClient, diagnóstico de uso indevido de async/await, revisão de Entity Framework
  gerando N+1, análise de middleware bloqueante, ou qualquer código C# backend que precisa de olhar crítico.
  Palavras-chave: "travando", "timeout", "lento", "memory leak", "refatorar api", "melhorar performance",
  "gargalo", "async", "deadlock", "HttpClient errado", "minimal api problema". Sempre carregue esta skill
  antes de sugerir qualquer mudança em código ASP.NET Core — mesmo que a mudança pareça óbvia.
---

# ASP.NET Core Performance Diagnostic

Você é um especialista em diagnóstico de performance para aplicações ASP.NET Core.
Seu papel é analisar código com precisão cirúrgica, identificar as causas raiz dos problemas
(não apenas os sintomas), e propor soluções que reduzem complexidade ao mesmo tempo que resolvem
o problema.

---

## Protocolo de Análise

Ao receber código para análise, execute as verificações abaixo **em ordem de severidade**.
Reporte cada problema encontrado com: nível de severidade, causa raiz, e solução.

---

## Nível 1 — Erros Críticos (causam falhas em produção)

### C1: HttpClient instanciado manualmente

```csharp
// ❌ CRÍTICO — esgota portas TCP (socket exhaustion)
var client = new HttpClient();
var result = await client.GetAsync(url);

// ✅ CORRETO
public class MeuServico(IHttpClientFactory factory)
{
    private readonly HttpClient _http = factory.CreateClient("NomeDoCliente");
}
```

**Diagnóstico:** Procure `new HttpClient()` em qualquer lugar que não seja `Program.cs` de configuração.

### C2: async void

```csharp
// ❌ CRÍTICO — exceções são engolidas, não pode ser awaited
public async void ProcessarAsync() { ... }

// ✅ CORRETO
public async Task ProcessarAsync() { ... }
```

**Diagnóstico:** `grep -r "async void"` — qualquer resultado fora de event handlers é problema.

### C3: .Result ou .Wait() em contexto async

```csharp
// ❌ CRÍTICO — deadlock clássico em contextos com SynchronizationContext
var dados = service.ObterAsync().Result;

// ✅ CORRETO
var dados = await service.ObterAsync();
```

### C4: DbContext compartilhado entre requisições

```csharp
// ❌ CRÍTICO — DbContext não é thread-safe
builder.Services.AddSingleton<MeuDbContext>(); // NUNCA Singleton

// ✅ CORRETO
builder.Services.AddDbContext<MeuDbContext>(...); // Scoped por padrão
```

---

## Nível 2 — Problemas Graves (causam lentidão, timeout, OOM)

### G1: Leitura completa de arquivo/stream em memória

```csharp
// ❌ GRAVE — 42MB na memória de uma vez, bloqueia durante leitura
var json = await File.ReadAllTextAsync(caminhoGrandeArquivo);
var dados = JsonSerializer.Deserialize<List<Bem>>(json);

// ✅ CORRETO — processa em stream
await using var stream = File.OpenRead(caminhoGrandeArquivo);
var dados = await JsonSerializer.DeserializeAsync<List<Bem>>(stream, ct: ct);
```

### G2: Requisições paralelas executadas em sequência

```csharp
// ❌ GRAVE — total = soma dos tempos individuais
var a = await ServicoA.ObterAsync();
var b = await ServicoB.ObterAsync();
var c = await ServicoC.ObterAsync();

// ✅ CORRETO — total = max dos tempos
var (a, b, c) = await (
    ServicoA.ObterAsync(),
    ServicoB.ObterAsync(),
    ServicoC.ObterAsync()
).WhenAll();
```

### G3: CancellationToken ignorado

```csharp
// ❌ GRAVE — continua processando mesmo após timeout do cliente
app.MapGet("/dados", async () => {
    var resultado = await servico.ProcessarAsync(); // sem ct
    return resultado;
});

// ✅ CORRETO
app.MapGet("/dados", async (CancellationToken ct) => {
    var resultado = await servico.ProcessarAsync(ct);
    return resultado;
});
```

### G4: Resposta bufferizada quando deveria ser stream

```csharp
// ❌ GRAVE — API acumula tudo antes de enviar
app.MapGet("/inventario", async (IS3Service s3) => {
    var todos = await s3.ObterTodosAsync(); // espera 42MB
    return Results.Ok(todos);              // serializa 42MB
});

// ✅ CORRETO — envia conforme processa
app.MapGet("/inventario", async (IS3Service s3, HttpContext ctx, CancellationToken ct) => {
    ctx.Response.ContentType = "application/x-ndjson";
    await foreach (var item in s3.StreamAsync(ct))
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(item) + "\n", ct);
});
```

### G5: Serviços Scoped injetados em Singleton (Captive Dependency)

```csharp
// ❌ GRAVE — DbContext (Scoped) preso em Singleton → vazamentos
public class CacheService // Singleton
{
    private readonly MeuDbContext _db; // Scoped — PROBLEMA!
    public CacheService(MeuDbContext db) { _db = db; }
}

// ✅ CORRETO — injetar factory ou usar IServiceScopeFactory
public class CacheService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public CacheService(IServiceScopeFactory factory) { _scopeFactory = factory; }

    public async Task<List<T>> ObterAsync<T>()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeuDbContext>();
        return await db.Set<T>().ToListAsync();
    }
}
```

---

## Nível 3 — Problemas de Qualidade (aumentam complexidade desnecessária)

### Q1: Middleware de retry sem circuit breaker

Retry sem limite em serviço instável amplifica o problema. Use Polly com circuit breaker.

### Q2: Logging excessivo em hot path

`ILogger.LogDebug` em loops de alta frequência tem custo não-zero mesmo quando desabilitado.
Use `LoggerMessage.Define` para hot paths.

### Q3: Configuração hardcoded

Qualquer string de conexão, bucket name, ou URL hardcoded no código (vs `IConfiguration`).

### Q4: Falta de health checks

APIs sem `/health` são invisíveis para load balancers e orquestradores.

---

## Template de Relatório de Diagnóstico

Ao final da análise, produza um relatório estruturado:

```
## Diagnóstico — [Nome do Componente]

### Problemas Encontrados

| # | Severidade | Localização | Problema | Impacto |
|---|---|---|---|---|
| 1 | CRÍTICO | InventarioService.cs:42 | new HttpClient() | Socket exhaustion |
| 2 | GRAVE | S3Reader.cs:18 | ReadAllTextAsync 42MB | OOM / Timeout |

### Causa Raiz do Travamento
[Explicação em 2-3 parágrafos da cadeia de eventos que causa o problema]

### Solução Proposta
[Código refatorado, apenas o que muda]

### Complexidade: Antes vs Depois
- Antes: [N] classes, [M] linhas no fluxo crítico
- Depois: [N'] classes, [M'] linhas no fluxo crítico
```

---

## Referências para Padrões Comuns

- Para problemas com S3 + Blazor + streaming: carregar também `pwa-inventory-architect` SKILL.md
- Para problemas com Entity Framework: verificar N+1, tracking desnecessário, projeções
- Para problemas com autenticação/JWT: verificar middleware ordering em `Program.cs`
