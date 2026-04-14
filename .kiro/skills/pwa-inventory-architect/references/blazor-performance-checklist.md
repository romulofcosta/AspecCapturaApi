# Checklist de Performance — Blazor WASM PWA

## Diagnóstico Rápido

Antes de otimizar, meça. Abra o DevTools (F12) e verifique:
- **Network tab:** Tamanho e duração das requisições de inventário
- **Performance tab:** Onde está o tempo (parsing JSON? rendering? network?)
- **Console:** Warnings de `[Warning] Skipping X frames` indicam thread bloqueada

---

## Checklist por Categoria

### HTTP e Requisições

- [ ] `HttpClient` criado via `IHttpClientFactory`, nunca `new HttpClient()`
- [ ] `HttpCompletionOption.ResponseHeadersRead` em downloads grandes
- [ ] `CancellationToken` passado em todas as chamadas async
- [ ] Timeout configurado explicitamente no cliente nomeado
- [ ] Requisições independentes disparadas em paralelo com `Task.WhenAll`

```csharp
// BOM: paralelo
var (bens, usuarios, orgaos) = await (
    CarregarBensAsync(orgao, ct),
    CarregarUsuariosAsync(ct),
    CarregarOrgaosAsync(ct)
).WhenAll();

// RUIM: sequencial desnecessário
var bens = await CarregarBensAsync(orgao, ct);
var usuarios = await CarregarUsuariosAsync(ct);
var orgaos = await CarregarOrgaosAsync(ct);
```

### Deserialização JSON

- [ ] Usar `JsonSerializer.DeserializeAsync` com stream, não com string
- [ ] Para arrays grandes, considerar `DeserializeAsyncEnumerable`
- [ ] `JsonSerializerOptions` reutilizado (singleton), não recriado por chamada
- [ ] Campos desnecessários excluídos com `[JsonIgnore]` ou `source generators`

```csharp
// BOM: stream direto
await using var stream = await response.Content.ReadAsStreamAsync(ct);
var itens = await JsonSerializer.DeserializeAsync<List<Bem>>(stream, _options, ct);

// RUIM: string intermediária (dobra uso de memória)
var json = await response.Content.ReadAsStringAsync(ct);
var itens = JsonSerializer.Deserialize<List<Bem>>(json, _options);
```

### Renderização de Componentes

- [ ] `StateHasChanged()` chamado em batch, não por item individual
- [ ] Listas grandes com virtualização (`<Virtualize>`)
- [ ] `@key` definido em loops `@foreach` para diff eficiente
- [ ] Componentes "folha" implementam `ShouldRender()` quando apropriado

```razor
@* BOM: virtualização para listas grandes *@
<Virtualize Items="_bens" Context="bem" OverscanCount="20">
    <ItemContent>
        <LinhaInventario @key="bem.Id" Bem="bem" />
    </ItemContent>
    <Placeholder>
        <LinhaSkeletonLoader />
    </Placeholder>
</Virtualize>
```

### Service Worker (PWA)

- [ ] Inventário com baixa frequência de mudança usa cache `CacheFirst`
- [ ] Inventário crítico usa `StaleWhileRevalidate`
- [ ] Cache invalidado quando nova versão do inventário é publicada no S3
- [ ] Tamanho do cache limitado para não esgotar storage do dispositivo

```javascript
// sw.js — estratégia recomendada para inventário patrimonial
registerRoute(
  ({ url }) => url.pathname.startsWith('/api/inventario/'),
  new StaleWhileRevalidate({
    cacheName: 'inventario-cache',
    plugins: [
      new ExpirationPlugin({
        maxAgeSeconds: 60 * 60 * 24 * 7, // 7 dias
        maxEntries: 50,
      }),
    ],
  })
);
```

### Estado e Memória

- [ ] Dados de inventário não mantidos em dois lugares simultaneamente
- [ ] Serviços `Scoped` (por sessão de navegação), não `Singleton` com dados de usuário
- [ ] `IDisposable` implementado em componentes com `CancellationTokenSource`
- [ ] Listas antigas limpas antes de carregar novos dados

```csharp
// Componente — cancelar stream ao navegar para outra página
public void Dispose()
{
    _cts.Cancel();
    _cts.Dispose();
}
```

---

## Métricas de Referência (metas razoáveis)

| Métrica | Ruim | Aceitável | Bom |
|---|---|---|---|
| Primeiro item visível | > 5s | 2-5s | < 2s |
| Carregamento completo (42MB) | > 30s | 10-30s | < 10s |
| UI freeze durante parse | > 2s | 0.5-2s | < 0.5s |
| Segunda visita (com cache) | > 3s | 1-3s | < 0.5s |
| Memória pico no browser | > 500MB | 200-500MB | < 200MB |

---

## Problemas Comuns e Soluções Rápidas

### "A tela trava por alguns segundos ao abrir o inventário"

**Causa:** Deserialização síncrona ou bloqueante no thread principal do WASM.
**Solução:** Garantir uso de `DeserializeAsync` com stream. Se ainda ocorrer, dividir o trabalho com `await Task.Yield()` entre lotes.

### "Timeout ao carregar inventário grande"

**Causa:** API tentando ler o arquivo completo antes de responder, timeout do Kestrel.
**Solução:** Usar streaming (padrão B do SKILL.md) ou presigned URL (padrão A).

### "Segunda requisição para o mesmo órgão é lenta"

**Causa:** Service Worker não configurado ou cache não funcionando.
**Solução:** Verificar registro do SW, confirmar estratégia de cache correta.

### "Erro 413 ou payload too large"

**Causa:** Nginx/API Gateway com limite de body size.
**Solução:** Remover limite para endpoints de upload, ou usar presigned URL para download direto do S3.

### "Memory pressure / OOM no servidor"

**Causa:** Múltiplos usuários simultâneos fazendo a API carregar o arquivo 42MB cada.
**Solução imediata:** Presigned URL (API sai do caminho dos dados).
**Solução alternativa:** Cache em memória do stream com `IMemoryCache` e `SemaphoreSlim`.
