# Configuração de CORS - PWA Camera PoC

## 📋 Resumo

Este documento explica a configuração de CORS (Cross-Origin Resource Sharing) implementada na API para permitir requisições do frontend Blazor PWA.

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
            }

            corsBuilder.WithOrigins(allowedOrigins.ToArray())
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials();
        });
});
```

## 🌐 Origens Permitidas

### Produção
- `https://pwa-camera-poc-blazor.pages.dev` - Frontend Blazor hospedado no Cloudflare Pages

### Desenvolvimento (apenas quando `ASPNETCORE_ENVIRONMENT=Development`)
- `https://localhost:5001`
- `http://localhost:5000`
- `https://localhost:7001`
- `http://localhost:7000`

## 🔧 Configuração do Frontend

### Arquivos de Configuração

#### `wwwroot/appsettings.Development.json`
```json
{
    "ApiBaseUrl": "http://localhost:5069"
}
```

#### `wwwroot/appsettings.Production.json`
```json
{
    "ApiBaseUrl": "https://pwa-camera-poc-api-production.up.railway.app"
}
```

### Uso no Program.cs do Blazor
```csharp
builder.Services.AddHttpClient("BackendApi", client => 
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5069";
    client.BaseAddress = new Uri(apiBaseUrl);
});
```

## 🚀 Como Adicionar Novas Origens

Se você precisar adicionar uma nova origem permitida (por exemplo, um novo domínio de produção):

1. Abra `pwa-camera-poc-api/Program.cs`
2. Localize a seção de configuração de CORS
3. Adicione a nova origem à lista `allowedOrigins`:

```csharp
var allowedOrigins = new List<string>
{
    "https://pwa-camera-poc-blazor.pages.dev",
    "https://seu-novo-dominio.com"  // Nova origem
};
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
1. Faça deploy da API no Railway
2. Faça deploy do Blazor no Railway
3. Acesse o frontend pelo domínio do Railway
4. Verifique se as requisições à API funcionam corretamente

## ⚠️ Problemas Comuns

### Erro: "CORS policy: No 'Access-Control-Allow-Origin' header"
**Causa**: A origem do frontend não está na lista de origens permitidas.

**Solução**: Adicione a origem à lista `allowedOrigins` no `Program.cs` da API.

### Erro: "CORS policy: The value of the 'Access-Control-Allow-Credentials' header"
**Causa**: Tentativa de usar `AllowAnyOrigin()` com `AllowCredentials()`.

**Solução**: Use `WithOrigins()` com origens específicas.

### Erro: "Mixed Content" (HTTP/HTTPS)
**Causa**: Frontend HTTPS tentando acessar API HTTP (ou vice-versa).

**Solução**: Certifique-se de que ambos usam o mesmo protocolo em produção (HTTPS).

## 📝 Checklist de Deploy

Antes de fazer deploy em produção:

- [ ] Verificar se a origem de produção está na lista `allowedOrigins`
- [ ] Confirmar que `appsettings.Production.json` aponta para a URL correta da API
- [ ] Testar requisições em ambiente de staging
- [ ] Verificar headers CORS no DevTools
- [ ] Confirmar que credenciais (se usadas) estão sendo enviadas corretamente
- [ ] Remover origens de desenvolvimento da lista de produção (se aplicável)

## 🔗 Referências

- [CORS no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cors)
- [MDN - CORS](https://developer.mozilla.org/en-US/docs/Web/HTTP/CORS)
- [Railway Deployment](https://docs.railway.app/)
