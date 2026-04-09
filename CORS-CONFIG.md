# Configuração de CORS - PWA Camera PoC

## 📋 Resumo

Este documento explica a configuração de CORS (Cross-Origin Resource Sharing) implementada na API para permitir requisições do frontend Blazor PWA hospedado no Cloudflare Pages.

## 🔒 Problema de Segurança Resolvido

### ❌ Configuração Anterior (Insegura)
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});
```

**Problema**: Esta configuração permitia que **qualquer site** fizesse requisições à API, representando um risco de segurança significativo.

### ✅ Configuração Atual (Segura)
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", corsBuilder =>
    {
        corsBuilder
            .SetIsOriginAllowed(origin =>
            {
                if (builder.Environment.IsDevelopment()) return true;
                
                // Permite o domínio principal e todos os subdomínios do Cloudflare Pages
                if (string.IsNullOrEmpty(origin)) return false;
                
                var uri = new Uri(origin);
                var host = uri.Host.ToLowerInvariant();
                
                return host == "pwa-camera-poc-blazor.pages.dev" ||
                       host.EndsWith(".pwa-camera-poc-blazor.pages.dev") ||
                       host.Contains("pwa-camera-poc-blazor.pages.dev");
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});
```

## 🌐 Origens Permitidas

### Produção
- `https://pwa-camera-poc-blazor.pages.dev` - Frontend Blazor hospedado no Cloudflare Pages (domínio principal)
- `https://[hash].pwa-camera-poc-blazor.pages.dev` - Subdomínios de preview do Cloudflare Pages

**Nota**: O Cloudflare Pages gera subdomínios únicos para cada deploy de preview (ex: `https://e82ab59d.pwa-camera-poc-blazor.pages.dev`). A configuração atual aceita todos esses subdomínios automaticamente.

### Desenvolvimento (apenas quando `ASPNETCORE_ENVIRONMENT=Development`)
- Todas as origens são permitidas para facilitar desenvolvimento local.

## 🔧 Configuração do Frontend

### Arquivos de Configuração

#### `wwwroot/appsettings.json`
```json
{
    "ApiBaseUrl": "https://pwa-camera-poc-api-production.up.railway.app"
}
```

**Importante**: Substitua pela URL real da sua API no Render.

### Uso no Program.cs do Blazor
```csharp
builder.Services.AddHttpClient("BackendApi", client => 
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
    if (string.IsNullOrEmpty(apiBaseUrl) || apiBaseUrl == "__API_BASE_URL__")
    {
        apiBaseUrl = "http://localhost:5069";
    }
    client.BaseAddress = new Uri(apiBaseUrl);
    // Aumenta o timeout para 10 minutos para operações de sincronização pesadas
    client.Timeout = TimeSpan.FromMinutes(10);
});
```

## 🚀 Como Adicionar Novas Origens

Se você precisar adicionar uma nova origem permitida (por exemplo, um novo domínio de produção):

1. Abra `pwa-camera-poc-api/Program.cs`
2. Localize a seção de configuração de CORS
3. Ajuste a lógica de `SetIsOriginAllowed` para atender seu domínio. Exemplo:

```csharp
return host == "pwa-camera-poc-blazor.pages.dev" ||
       host.EndsWith(".pwa-camera-poc-blazor.pages.dev") ||
       host.Contains("pwa-camera-poc-blazor.pages.dev") ||
       host == "seu-dominio-customizado.com";
```

## 🔐 Recursos de Segurança

### `AllowCredentials()`
Permite o envio de cookies e tokens de autenticação nas requisições. Isso é essencial para:
- Autenticação baseada em cookies
- Tokens JWT armazenados em cookies HttpOnly
- Sessões autenticadas

### `WithOrigins()` vs `AllowAnyOrigin()`
- ✅ `WithOrigins()`: Especifica exatamente quais domínios podem acessar a API
- ❌ `AllowAnyOrigin()`: Permite qualquer domínio (inseguro para produção)

**Importante**: Quando você usa `AllowCredentials()`, **não pode** usar `AllowAnyOrigin()`. Você deve especificar origens explícitas.

## 🧪 Testando CORS

### Teste Local
1. Execute a API: `dotnet run` (em `pwa-camera-poc-api`)
2. Execute o Blazor: `dotnet run` (em `pwa-camera-poc-blazor`)
3. Abra o navegador em `http://localhost:5000`
4. Abra o DevTools (F12) e verifique a aba Network
5. Faça uma requisição à API e verifique os headers de resposta:
   - `Access-Control-Allow-Origin: http://localhost:5000`
   - `Access-Control-Allow-Credentials: true`

### Teste de Produção
1. Faça deploy da API no Render
2. Faça deploy do Blazor no Cloudflare Pages
3. Acesse o frontend pelo domínio do Cloudflare Pages
4. Verifique se as requisições à API funcionam corretamente
5. Teste também os deploys de preview (subdomínios com hash)

## ⚠️ Problemas Comuns

### Erro: "CORS policy: No 'Access-Control-Allow-Origin' header"
**Causa**: A origem do frontend não está na lista de origens permitidas.

**Solução**: 
1. Verifique se a origem está sendo validada corretamente na lógica de `SetIsOriginAllowed`
2. Confirme que o domínio do Cloudflare Pages corresponde ao padrão esperado
3. Verifique os logs da API para ver qual origem está sendo recebida

### Erro: "CORS policy: The value of the 'Access-Control-Allow-Credentials' header"
**Causa**: Tentativa de usar `AllowAnyOrigin()` com `AllowCredentials()`.

**Solução**: Use `SetIsOriginAllowed()` com origens específicas (já implementado).

### Erro: "Mixed Content" (HTTP/HTTPS)
**Causa**: Frontend HTTPS tentando acessar API HTTP (ou vice-versa).

**Solução**: Certifique-se de que ambos usam o mesmo protocolo em produção (HTTPS).

### Erro: Subdomínios de Preview Bloqueados
**Causa**: A configuração de CORS não aceita os subdomínios gerados pelo Cloudflare Pages.

**Solução**: A configuração atual já resolve isso usando:
```csharp
host.EndsWith(".pwa-camera-poc-blazor.pages.dev") ||
host.Contains("pwa-camera-poc-blazor.pages.dev")
```

## 📝 Checklist de Deploy

Antes de fazer deploy em produção:

- [x] Verificar se a origem de produção está na lista de origens permitidas
- [x] Confirmar que `appsettings.json` aponta para a URL correta da API
- [x] Configuração de CORS aceita subdomínios do Cloudflare Pages
- [x] Timeout do HttpClient aumentado para 10 minutos
- [ ] Testar requisições em ambiente de staging
- [ ] Verificar headers CORS no DevTools
- [ ] Confirmar que credenciais (se usadas) estão sendo enviadas corretamente
- [ ] Remover origens de desenvolvimento da lista de produção (se aplicável)
- [ ] Testar deploys de preview (subdomínios com hash)

## 🔗 Referências

- [CORS no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cors)
- [MDN - CORS](https://developer.mozilla.org/en-us/docs/Web/HTTP/CORS)
- [Cloudflare Pages Deployment](https://developers.cloudflare.com/pages)
- [Render Deployment](https://render.com/docs)
