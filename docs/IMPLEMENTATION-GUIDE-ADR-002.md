# Guia de Implementação — ADR-002: Simplificar Sincronização

**Data:** 14 de abril de 2026  
**Responsável:** Kiro (Arquiteto) + Rômulo (Desenvolvedor)  
**Referência:** [ADR-002](./ADR-002-simplify-data-sync.md)

---

## 📋 Checklist de Implementação

### ✅ Fase 1: Backend (AspecCapturaApi)

- [ ] 1.1. Otimizar compressão (Fastest → Optimal)
- [ ] 1.2. Criar novo endpoint `/api/tombamentos`
- [ ] 1.3. Deprecar endpoints antigos (manter por 1 mês)
- [ ] 1.4. Build + testes
- [ ] 1.5. Deploy em stage

### ✅ Fase 2: Frontend (AspecCaptura)

- [ ] 2.1. Atualizar `TombamentoSyncService.cs`
- [ ] 2.2. Remover lógica de chunking
- [ ] 2.3. Adicionar loading com estimativa de tempo
- [ ] 2.4. Build (ou getDiagnostics se wasm-tools não disponível)
- [ ] 2.5. Deploy em stage

### ✅ Fase 3: Validação

- [ ] 3.1. Testar em stage (DevTools Network)
- [ ] 3.2. Testar em mobile (celular de entrada)
- [ ] 3.3. Validar métricas (tempo, memória, UX)
- [ ] 3.4. Decisão: prosseguir ou reverter

### ✅ Fase 4: Produção

- [ ] 4.1. Deploy em prod
- [ ] 4.2. Monitorar por 1 semana
- [ ] 4.3. Remover código de chunking (se sucesso)
- [ ] 4.4. Atualizar documentação

---

## 🔧 Mudanças no Código

### 1. Backend: Otimizar Compressão

**Arquivo:** `AspecCapturaApi/Program.cs`  
**Linha:** ~170

**ANTES:**
```csharp
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
```

**DEPOIS:**
```csharp
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
```

**Impacto:**
- Compressão: 61% → 75% (14MB → 9MB para 36MB JSON)
- CPU servidor: +100ms por request
- Tempo download (3G): 56s → 36s

---

### 2. Backend: Novo Endpoint Simplificado

**Arquivo:** `AspecCapturaApi/Program.cs`  
**Localização:** Após o endpoint `/api/auth/login`, antes dos endpoints de chunking

**Adicionar:**

```csharp
// ─── ENDPOINT: GET /api/tombamentos (ADR-002: Carga única) ───────────────────
app.MapGet("/api/tombamentos", 
    [Authorize] async (
    [FromQuery] string prefix,
    ClaimsPrincipal user,
    [FromServices] IAmazonS3 s3,
    [FromServices] IConfiguration config,
    [FromServices] IMemoryCache cache,
    ILogger<Program> log) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            log.LogWarning("Tombamentos: prefix vazio ou nulo");
            return Results.BadRequest(new { error = "prefix é obrigatório" });
        }

        // ─── SECURITY: Validar autorização (usuário só acessa seu município) ─────
        var userPrefix = user.FindFirst("prefix")?.Value;
        if (userPrefix != prefix.ToUpper())
        {
            log.LogWarning("Unauthorized: user {User} tried to access {Prefix}", 
                user.Identity?.Name, prefix);
            return Results.Forbid();
        }

        var key = $"usuarios/{prefix.ToUpper()}.json";
        var bucket = config["AWS:BucketName"];
        
        if (string.IsNullOrEmpty(bucket))
        {
            log.LogError("Tombamentos: Bucket não configurado");
            return Results.Problem("Bucket não configurado.");
        }

        // Verifica se o arquivo existe
        GetObjectMetadataResponse head;
        try
        {
            head = await s3.GetObjectMetadataAsync(bucket, key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            log.LogWarning("Tombamentos: Arquivo não encontrado: {Key}", key);
            return Results.NotFound(new { error = $"Prefixo '{prefix}' não encontrado" });
        }

        var version = (head.LastModified ?? DateTime.UtcNow).ToUniversalTime().ToString("yyyyMMddHHmmss");
        
        // Carrega dados unificados (reutiliza cache existente)
        var data = await GetOrLoadUnifiedDataAsync(s3, bucket, key, version, cache, jsonOptions, log);
        
        // Filtra tombamentos por exercício fiscal corrente
        var exercicioCorrente = DateTime.Now.Year;
        var tombamentos = data.Tabelas?.Tombamentos?
            .Where(t => t.ExercicioFiscal == exercicioCorrente || t.ExercicioFiscal == 0)
            .ToList() ?? new List<TombamentoRecord>();

        log.LogInformation("Tombamentos: Retornando {Count} registros para {Prefix} (versão {Version})", 
            tombamentos.Count, prefix, version);

        // Retorna tombamentos (middleware comprime automaticamente com Brotli Optimal)
        return Results.Ok(tombamentos);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Tombamentos: Erro ao processar para prefix={Prefix}", prefix);
        return Results.Problem(
            title: "Erro ao carregar tombamentos",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.WithName("GetTombamentos")
.WithOpenApi()
.WithTags("Tombamentos")
.RequireAuthorization();
```

**Características:**
- ✅ Autenticação obrigatória (`[Authorize]`)
- ✅ Validação de prefixo (usuário só acessa seu município)
- ✅ Reutiliza cache existente (`GetOrLoadUnifiedDataAsync`)
- ✅ Filtra por exercício fiscal corrente
- ✅ Compressão automática via middleware (Brotli Optimal)
- ✅ Logs estruturados
- ✅ Tratamento de erros completo

---

### 3. Backend: Deprecar Endpoints Antigos

**Arquivo:** `AspecCapturaApi/Program.cs`

**Adicionar comentário antes dos endpoints de chunking:**

```csharp
// ─── DEPRECATED: Endpoints de chunking (ADR-002) ──────────────────────────────
// Mantidos por 1 mês para compatibilidade com versões antigas do frontend.
// Remover após 14 de maio de 2026.
// Usar /api/tombamentos (carga única) ao invés de /sync-info + /lote/{id}

app.MapGet("/api/tombamentos/sync-info", async (...) => { ... })
    .WithName("TombamentosSyncInfo")
    .WithOpenApi()
    .WithTags("Tombamentos (Deprecated)");  // ← Marcar como deprecated

app.MapGet("/api/tombamentos/lote/{id:int}", async (...) => { ... })
    .WithName("TombamentosChunk")
    .WithOpenApi()
    .WithTags("Tombamentos (Deprecated)");  // ← Marcar como deprecated
```

**Não remover ainda** — manter por 1 mês como fallback.

---

### 4. Frontend: Atualizar Sincronização

**Arquivo:** `AspecCaptura/Services/TombamentoSyncService.cs` (ou onde estiver a lógica)

**ANTES (complexo — ~50 linhas):**
```csharp
public async Task<List<Tombamento>> SyncTombamentosAsync(string prefix)
{
    var tombamentos = new List<Tombamento>();
    
    // 1. Obter metadados de chunks
    var syncInfo = await Http.GetFromJsonAsync<SyncInfo>(
        $"{ApiBaseUrl}/api/tombamentos/sync-info?prefix={prefix}");
    
    if (syncInfo == null || syncInfo.TotalChunks == 0)
        return tombamentos;
    
    // 2. Baixar chunks sequencialmente
    for (int i = 1; i <= syncInfo.TotalChunks; i++)
    {
        try
        {
            var chunk = await Http.GetFromJsonAsync<List<Tombamento>>(
                $"{ApiBaseUrl}/api/tombamentos/lote/{i}?prefix={prefix}");
            
            if (chunk != null)
            {
                tombamentos.AddRange(chunk);
                
                // Atualizar UI progressivamente
                await OnProgressChanged?.Invoke(i, syncInfo.TotalChunks);
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Erro ao baixar chunk {ChunkId}", i);
            // Continua com próximo chunk
        }
    }
    
    return tombamentos;
}
```

**DEPOIS (simples — 1 linha):**
```csharp
public async Task<List<Tombamento>> SyncTombamentosAsync(string prefix)
{
    try
    {
        var tombamentos = await Http.GetFromJsonAsync<List<Tombamento>>(
            $"{ApiBaseUrl}/api/tombamentos?prefix={prefix}");
        
        return tombamentos ?? new List<Tombamento>();
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        Logger.LogWarning("Prefixo {Prefix} não encontrado", prefix);
        return new List<Tombamento>();
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Erro ao sincronizar tombamentos para {Prefix}", prefix);
        throw;
    }
}
```

**Redução:** 50 linhas → 15 linhas (70% menos código)

---

### 5. Frontend: Loading com Estimativa

**Arquivo:** `AspecCaptura/Pages/Login.razor` (ou componente de sincronização)

**Adicionar:**

```razor
@if (isSyncing)
{
    <div class="loading-overlay">
        <div class="loading-content">
            <MudProgressCircular Indeterminate="true" Size="Size.Large" />
            <p class="mt-4">Sincronizando tombamentos...</p>
            <p class="text-muted">Isso pode levar até 40 segundos em redes 3G</p>
            <p class="text-sm text-muted mt-2">
                Aguarde enquanto baixamos @estimatedSize de dados
            </p>
        </div>
    </div>
}

@code {
    private bool isSyncing = false;
    private string estimatedSize = "~9MB";
    
    private async Task SyncData()
    {
        isSyncing = true;
        StateHasChanged();
        
        try
        {
            var tombamentos = await TombamentoService.SyncTombamentosAsync(userPrefix);
            // ... processar tombamentos
        }
        finally
        {
            isSyncing = false;
            StateHasChanged();
        }
    }
}
```

**Melhoria de UX:**
- Usuário sabe que vai demorar (~40s)
- Sabe quanto está baixando (~9MB)
- Não fica ansioso achando que travou

---

## 🧪 Testes

### Teste 1: Tempo de Resposta (DevTools)

1. Abrir DevTools → Network tab
2. Fazer login em stage
3. Observar request `GET /api/tombamentos?prefix=CE999`
4. Verificar:
   - **Size:** ~36MB (descomprimido)
   - **Transferred:** ~9MB (comprimido)
   - **Time:** < 40s em 3G simulado
   - **Content-Encoding:** br (Brotli)

**Critério de sucesso:** Tempo < 40s, tamanho transferido < 10MB

---

### Teste 2: Memória do Servidor (Render Logs)

1. Acessar Render Dashboard → Logs
2. Fazer 3 logins simultâneos (abas diferentes)
3. Observar logs de memória
4. Verificar:
   - Pico de memória < 400MB
   - Sem erros de OOM (Out of Memory)
   - Sem timeouts (30s)

**Critério de sucesso:** Sem OOM, sem timeout, memória < 400MB

---

### Teste 3: Descompressão Mobile

1. Abrir Chrome DevTools → Performance
2. Fazer login em celular (ou simulador)
3. Gravar performance durante sincronização
4. Verificar:
   - Tempo de descompressão < 50ms
   - Sem travamentos (FPS > 30)
   - Sem GC pause > 100ms

**Critério de sucesso:** Descompressão < 50ms, sem travamentos

---

### Teste 4: Múltiplos Usuários (k6 Load Test)

**Script k6:**
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  stages: [
    { duration: '30s', target: 5 },  // Ramp up to 5 users
    { duration: '1m', target: 5 },   // Stay at 5 users
    { duration: '30s', target: 0 },  // Ramp down
  ],
};

export default function () {
  const loginRes = http.post('https://aspec-capture-api.onrender.com/api/auth/login', 
    JSON.stringify({
      usuario: 'ce999.teste.usuario',
      senha: 'senha123'
    }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  
  check(loginRes, {
    'login status 200': (r) => r.status === 200,
  });
  
  const token = loginRes.json('token');
  
  const tombRes = http.get(
    'https://aspec-capture-api.onrender.com/api/tombamentos?prefix=CE999',
    { headers: { 'Authorization': `Bearer ${token}` } }
  );
  
  check(tombRes, {
    'tombamentos status 200': (r) => r.status === 200,
    'response time < 40s': (r) => r.timings.duration < 40000,
  });
  
  sleep(1);
}
```

**Executar:**
```bash
k6 run load-test.js
```

**Critério de sucesso:** 
- 95% das requests < 40s
- 0% de erros 500
- 0% de timeouts

---

## 📊 Métricas de Validação

| Métrica | Alvo | Como Medir |
|---------|------|------------|
| Tempo de resposta (P95) | < 40s | DevTools Network |
| Banda transferida | < 10MB | DevTools Network (Transferred) |
| Memória servidor (pico) | < 400MB | Render Logs |
| Descompressão mobile | < 50ms | Chrome DevTools Performance |
| Taxa de sucesso | > 95% | Logs do servidor |
| Timeouts | 0% | Render Logs |

---

## 🔄 Plano de Rollback

**Se alguma métrica falhar:**

1. **Reverter backend:**
   ```bash
   cd AspecCapturaApi
   git revert HEAD
   git push origin desenvolvimento_v3
   # Render faz redeploy automático em ~2 minutos
   ```

2. **Reverter frontend:**
   ```bash
   cd AspecCaptura
   git revert HEAD
   git push origin desenvolvimento_v3
   # Cloudflare Pages faz redeploy automático em ~1 minuto
   ```

3. **Validar rollback:**
   - Testar login + sincronização em stage
   - Verificar que chunking voltou a funcionar
   - Confirmar que não há erros

**Tempo total de rollback:** ~5 minutos

---

## 📅 Timeline

| Fase | Duração | Responsável |
|------|---------|-------------|
| 1. Implementação backend | 1 hora | Kiro |
| 2. Implementação frontend | 1 hora | Kiro |
| 3. Build + testes locais | 30 min | Kiro |
| 4. Deploy em stage | 10 min | Automático |
| 5. Testes em stage | 2 horas | Rômulo |
| 6. Decisão go/no-go | 10 min | Rômulo + Kiro |
| 7. Deploy em prod | 10 min | Automático |
| 8. Monitoramento | 1 semana | Rômulo |
| 9. Remoção de código antigo | 1 hora | Kiro |

**Total:** ~5 horas de trabalho + 1 semana de monitoramento

---

## ✅ Critérios de Sucesso Final

**Para considerar a mudança bem-sucedida:**

1. ✅ Tempo de sincronização < 40s (P95)
2. ✅ Sem timeouts no Render (0% em 1 semana)
3. ✅ Sem OOM no servidor (0% em 1 semana)
4. ✅ Sem reclamações de usuários sobre lentidão
5. ✅ Sem travamentos em mobile (testado em celular de entrada)
6. ✅ Taxa de sucesso de sincronização > 95%

**Se todos os critérios passarem:**
- Remover código de chunking (`BuildOrGetChunkIndexAsync`, endpoints deprecated)
- Atualizar documentação
- Fechar ADR-002 como "Implementado com sucesso"

**Se algum critério falhar:**
- Reverter usando procedimento acima
- Documentar motivo da falha no ADR-002
- Manter chunking como solução permanente

---

## 📝 Notas Finais

- **Não apressar:** Monitorar por 1 semana completa antes de remover código antigo
- **Ser conservador:** Ao menor sinal de problema, reverter imediatamente
- **Documentar tudo:** Logs, métricas, feedback de usuários
- **Aprender:** Se falhar, entender o porquê e documentar no ADR

**Lembre-se:** Simplicidade é o objetivo, mas não a qualquer custo. Se a mudança causar problemas reais, reverter é a decisão certa.

---

**Última atualização:** 14 de abril de 2026  
**Próxima revisão:** Após testes em stage
