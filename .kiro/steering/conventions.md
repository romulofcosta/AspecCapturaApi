# Convenções do Projeto AspecCapturaApi

## Versionamento

- Ambos os projetos (AspecCaptura e AspecCapturaApi) são versionados juntos com a mesma versão
- Padrão semântico: `MAJOR.MINOR.PATCH` (ex: `0.11.0`)
- **Antes de qualquer commit**, atualizar a versão nos seguintes arquivos:

### AspecCaptura (frontend)
- `AspecCaptura/AspecCaptura.csproj` — `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`
- `AspecCaptura/wwwroot/manifest.json` — campo `"version"`
- `AspecCaptura/wwwroot/service-worker.js` — constante `APP_VERSION`
- `AspecCaptura/Pages/Login.razor` — texto de versão visível na tela (feedback visual ao usuário)

### AspecCapturaApi (backend)
- `AspecCapturaApi/AspecCapturaApi.csproj` — `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`
- `AspecCapturaApi/Program.cs` — SwaggerDoc version se presente

## Padrão de Commit

```
v{VERSAO} - {Descrição resumida das mudanças}
```

Exemplos do histórico:
- `v0.11.1 - Correção de label/placeholder do campo usuário no login e melhorias de performance`
- `v0.10.0 - Refatoração: Design System Completo e Melhorias de Segurança`
- `v0.9.0 - Renomeação do Projeto e Melhorias de Configuração`
- `v0.8.0 - Correção de CORS e adição de versionamento`

## Branch de desenvolvimento

- Branch ativa: `desenvolvimento_v3`
- Tags são criadas no commit de versão: `git tag v0.11.1`

## Regra geral

Nunca commitar sem versionar. O feedback visual da versão na tela de login é obrigatório.

## Build e Testes (pré-commit)

### AspecCapturaApi
- `dotnet build AspecCapturaApi.csproj -c Release` — deve passar sem erros
- `dotnet build tests/Tests.csproj -c Release` — deve passar sem erros
- `dotnet test tests/Tests.csproj -c Release --no-build` — todos os 14 testes devem passar

### AspecCaptura
- O build local requer o workload `wasm-tools` instalado com privilégios de administrador
- Sem o workload, o build local falha com NETSDK1147 — isso é esperado em ambientes sem admin
- O build real é validado pelo CI no Cloudflare Pages via `build.sh`

## Formato de usuário

- O campo de login aceita o formato `municipio.nome.sobrenome` (ex: `ce999.joao.silva`)
- Não é e-mail — o placeholder deve refletir isso: `municipio.nome.sobrenome`
- A API valida que o prefixo do token JWT bate com o prefixo da requisição (segurança por município)

## Dockerfile

- O `Dockerfile` referencia `AspecCapturaApi.csproj` e `AspecCapturaApi.dll` (nome atual)
- Nome antigo `pwa-camera-poc-api` foi descontinuado — nunca usar em novos arquivos
- Se o projeto for renomeado novamente, atualizar: `COPY`, `RUN dotnet restore`, `RUN dotnet build`, `RUN dotnet publish`, e `ENTRYPOINT`

## Testes — Autenticação

- Os testes de integração usam `CaptureTestFactory.CreateAuthenticatedClient()`
- O JWT de teste usa a constante `TestJwtSecret` definida na factory
- Endpoints protegidos com `[Authorize]` retornam `403` quando o prefixo do token não bate com o da requisição
- `JWT_SECRET` deve ser injetado via `Environment.SetEnvironmentVariable` na factory para testes
