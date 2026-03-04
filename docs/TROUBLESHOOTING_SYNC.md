# Troubleshooting - Sincronização de Tombamentos

Guia de resolução de problemas relacionados ao fluxo de sincronização de tombamentos.

## 🚨 Problema: Erro 500 na Tela "Baixando Lotes de Tombamentos"

### Sintomas
- Tela de "Baixando Lotes de Tombamentos" trava
- Não avança para a tela de seleção de sessão
- Erro 500 retornado pela API
- Console do navegador mostra erro na chamada `/api/tombamentos/sync-info`

### Causa Raiz Identificada

#### 1. **NullReferenceException ao Processar Itens**
```csharp
// ❌ CÓDIGO PROBLEMÁTICO
var itens = JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, options)
    .Result?.Tabelas?.Tombamentos.ToList();

foreach (var item in itens) // ← NullReferenceException se itens for null
{
    // ...
}
```

**Problema:**
- Uso de `.Result` (bloqueante) em método async
- Não valida se `itens` é null antes do `foreach`
- Se o arquivo JSON não tiver a estrutura esperada, `itens` será null

#### 2. **Falta de Tratamento de Erro nos Endpoints**
```csharp
// ❌ CÓDIGO PROBLEMÁTICO
app.MapGet("/api/tombamentos/sync-info", async (...) =>
{
    // Sem try-catch
    var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache);
    return Results.Ok(...);
});
```

**Problema:**
- Exceções não capturadas retornam erro 500 genérico
- Logs insuficientes para diagnóstico
- Cliente não recebe mensagem de erro clara

#### 3. **Inconsistência no Método SerializeChunkPayloadAsync**
```csharp
// ❌ CÓDIGO PROBLEMÁTICO
await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<TombamentoRecord>(...))
{
    // Espera array JSON direto, mas arquivo tem estrutura UnifiedDataRecord
}
```

**Problema:**
- `DeserializeAsyncEnumerable` espera array JSON `[{}, {}]`
- Arquivo real tem estrutura `{ "tabelas": { "tombamentos": [...] } }`
- Falha silenciosa ou exceção

---

## ✅ Correções Aplicadas

### Correção 1: Validação de Null e Uso de Await

**ANTES:**
```csharp
var itens = JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, options)
    .Result?.Tabelas?.Tombamentos.ToList();

foreach (var item in itens) // ← Pode lançar NullReferenceException
{
    // ...
}
```

**DEPOIS:**
```csharp
// ✅ Usar await ao invés de .Result
var unifiedData = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, options);
var itens = unifiedData?.Tabelas?.Tombamentos?.ToList();

// ✅ Validação crítica: se itens for null, retorna index vazio
if (itens is null || itens.Count == 0)
{
    Console.WriteLine($"[AVISO] Nenhum tombamento encontrado em {key}");
    var emptyIndex = new ChunkIndex(version, 0, "", new List<ChunkMeta>(), options);
    cache.Set(cacheKey, emptyIndex, TimeSpan.FromHours(1));
    return emptyIndex;
}

Console.WriteLine($"[INFO] Processando {itens.Count} tombamentos de {key}");

foreach (var item in itens) // ✅ Seguro agora
{
    // ...
}
```

### Correção 2: Tratamento de Erro nos Endpoints

**ANTES:**
```csharp
app.MapGet("/api/tombamentos/sync-info", async (...) =>
{
    if (string.IsNullOrWhiteSpace(prefix)) return Results.BadRequest(...);
    var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache);
    return Results.Ok(...);
});
```

**DEPOIS:**
```csharp
app.MapGet("/api/tombamentos/sync-info", async (...) =>
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
        
        // ✅ Verifica se o arquivo existe antes de processar
        try
        {
            await s3.GetObjectMetadataAsync(bucket, key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            log.LogWarning("SyncInfo: Arquivo não encontrado: {Key}", key);
            return Results.NotFound(new { error = $"Arquivo {key} não encontrado" });
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
});
```

### Correção 3: SerializeChunkPayloadAsync Consistente

**ANTES:**
```csharp
static async Task<byte[]> SerializeChunkPayloadAsync(...)
{
    using var obj = await s3.GetObjectAsync(bucket, key);
    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<TombamentoRecord>(...))
    {
        // ❌ Espera array JSON direto
    }
}
```

**DEPOIS:**
```csharp
static async Task<byte[]> SerializeChunkPayloadAsync(...)
{
    using var obj = await s3.GetObjectAsync(bucket, key);
    using var stream = obj.ResponseStream;
    
    // ✅ Usar UnifiedDataRecord (estrutura correta)
    var unifiedData = await JsonSerializer.DeserializeAsync<UnifiedDataRecord>(stream, options);
    var allItems = unifiedData?.Tabelas?.Tombamentos?.ToList();
    
    if (allItems is null || allItems.Count == 0)
    {
        Console.WriteLine($"[AVISO] SerializeChunkPayload: Nenhum tombamento encontrado");
        return JsonSerializer.SerializeToUtf8Bytes(result, options);
    }
    
    // ✅ Extrai apenas os itens do chunk especificado
    for (int i = start - 1; i < end && i < allItems.Count; i++)
    {
        var item = allItems[i];
        if (item is not null)
        {
            result.data.Add(item);
        }
    }
    
    return JsonSerializer.SerializeToUtf8Bytes(result, options);
}
```

---

## 🔍 Como Diagnosticar

### 1. Verificar Logs da API

```bash
# Logs esperados em caso de sucesso:
[INFO] SyncInfo: Processando key=usuarios/CE999.json
[INFO] Processando 50000 tombamentos de usuarios/CE999.json
[CHUNK] Finalizado: 5000 registros, 1024 KB
[CHUNK] Finalizado: 5000 registros, 1018 KB
...
[INFO] SyncInfo: Sucesso - 50000 registros, 10 chunks

# Logs em caso de erro:
[AVISO] Nenhum tombamento encontrado em usuarios/CE999.json
[ERROR] SyncInfo: Erro ao processar sync-info para prefix=CE999
```

### 2. Verificar Console do Navegador

```javascript
// Erro esperado ANTES da correção:
Failed to load resource: the server responded with a status of 500 (Internal Server Error)
/api/tombamentos/sync-info?prefix=CE999

// Sucesso DEPOIS da correção:
SyncInfo: { totalRegistros: 50000, totalChunks: 10, versao: "20260210143000", hashGlobal: "..." }
```

### 3. Testar Endpoint Manualmente

```bash
# Teste com curl
curl -X GET "http://localhost:5000/api/tombamentos/sync-info?prefix=CE999" \
  -H "Accept: application/json"

# Resposta esperada:
{
  "totalRegistros": 50000,
  "totalChunks": 10,
  "versao": "20260210143000",
  "hashGlobal": "A1B2C3..."
}

# Erro esperado se arquivo não existir:
{
  "error": "Arquivo usuarios/CE999.json não encontrado no bucket ..."
}
```

---

## 🧪 Testes de Validação

### Teste 1: Arquivo Válido

```csharp
// Cenário: Arquivo CE999.json existe e tem tombamentos
var response = await client.GetAsync("/api/tombamentos/sync-info?prefix=CE999");
Assert.Equal(HttpStatusCode.OK, response.StatusCode);

var info = await response.Content.ReadFromJsonAsync<SyncInfoDto>();
Assert.NotNull(info);
Assert.True(info.totalRegistros > 0);
Assert.True(info.totalChunks > 0);
```

### Teste 2: Arquivo Não Encontrado

```csharp
// Cenário: Arquivo INEXISTENTE.json não existe
var response = await client.GetAsync("/api/tombamentos/sync-info?prefix=INEXISTENTE");
Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
Assert.Contains("não encontrado", error.error);
```

### Teste 3: Arquivo Vazio

```csharp
// Cenário: Arquivo existe mas não tem tombamentos
var response = await client.GetAsync("/api/tombamentos/sync-info?prefix=VAZIO");
Assert.Equal(HttpStatusCode.OK, response.StatusCode);

var info = await response.Content.ReadFromJsonAsync<SyncInfoDto>();
Assert.Equal(0, info.totalRegistros);
Assert.Equal(0, info.totalChunks);
```

### Teste 4: Prefix Inválido

```csharp
// Cenário: Prefix vazio ou null
var response = await client.GetAsync("/api/tombamentos/sync-info?prefix=");
Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
Assert.Contains("obrigatório", error.error);
```

---

## 📋 Checklist de Verificação

Antes de considerar o problema resolvido, verifique:

- [ ] API compila sem erros
- [ ] Logs mostram `[INFO] Processando X tombamentos`
- [ ] Endpoint `/api/tombamentos/sync-info` retorna 200 OK
- [ ] Endpoint `/api/tombamentos/lote/{id}` retorna 200 OK
- [ ] Frontend avança para tela de seleção de sessão
- [ ] Nenhum erro 500 no console do navegador
- [ ] Logs não mostram `NullReferenceException`
- [ ] Cache está funcionando (segunda chamada é mais rápida)

---

## 🚀 Próximos Passos

### Melhorias Recomendadas

1. **Validação de Estrutura JSON**
   ```csharp
   // Validar se o JSON tem a estrutura esperada
   if (unifiedData?.Tabelas is null)
   {
       throw new InvalidOperationException("JSON não tem estrutura UnifiedDataRecord");
   }
   ```

2. **Retry Automático no Frontend**
   ```csharp
   // Já implementado em SyncService.DownloadWithRetryAsync
   // Mas pode ser melhorado com exponential backoff
   ```

3. **Health Check Endpoint**
   ```csharp
   app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
   ```

4. **Monitoramento de Performance**
   ```csharp
   // Adicionar métricas de tempo de processamento
   var sw = Stopwatch.StartNew();
   var index = await BuildOrGetChunkIndexAsync(...);
   sw.Stop();
   log.LogInformation("SyncInfo processado em {Elapsed}ms", sw.ElapsedMilliseconds);
   ```

---

## 📞 Suporte

**Se o problema persistir:**

1. Verifique os logs da API (`console` ou arquivo de log)
2. Verifique o console do navegador (F12)
3. Teste o endpoint manualmente com curl/Postman
4. Verifique se o arquivo S3 existe e tem a estrutura correta
5. Verifique se as credenciais AWS estão configuradas

**Arquivos Relacionados:**
- `pwa-camera-poc-api/Program.cs` - Endpoints e lógica de chunking
- `pwa-camera-poc-blazor/Services/Sync/SyncService.cs` - Cliente de sincronização
- `pwa-camera-poc-api/docs/PERFORMANCE_OPTIMIZATION.md` - Otimizações aplicadas

---

**Última Atualização:** 2026-03-04  
**Versão:** 0.3.1  
**Autor:** Equipe ASPEC
