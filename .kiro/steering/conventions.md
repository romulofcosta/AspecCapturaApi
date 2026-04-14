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

## Mantra de Qualidade — Build e Testes

**Obrigatório após qualquer alteração de código:**
1. Rodar build e testes automatizados
2. Reportar resultado antes de pedir teste manual ao usuário
3. Repetir antes de qualquer commit

### AspecCapturaApi
```
dotnet build AspecCapturaApi.csproj -c Release
dotnet build tests/Tests.csproj -c Release
dotnet test tests/Tests.csproj -c Release --no-build
```

### AspecCaptura
```
dotnet build AspecCaptura.csproj -c Release
# (requer wasm-tools com admin — se falhar NETSDK1147, é limitação local, validar no CI)
```

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

## Regras de Negócio — Scan e Localização

### Comportamento do Scan (intencional)
- O scan **não filtra** por área/subárea configurada na sessão — isso é correto por regra de negócio
- Um bem pode estar fisicamente em uma área diferente da que está registrada (transferências, deslocamentos)
- O usuário configura **onde está fisicamente** (sessão atual) e escaneia o bem onde ele se encontra
- O sistema deve **mostrar a divergência** quando a localização registrada ≠ localização da sessão
- O usuário precisa conseguir identificar visualmente que o bem pertence a outra área/órgão

### Roadmap — Flag de Deslocamento (próxima versão)
- Quando houver divergência de localização, o usuário poderá marcar o bem como "deslocado"
- Isso indicará que o bem foi movido para outra área/órgão desde o último inventário
- Definições detalhadas a serem especificadas em próxima iteração

## Princípios de Código

Todo código novo ou refatorado deve seguir esses princípios. São critérios de revisão obrigatórios antes de qualquer commit.

- **KISS** (Keep It Simple, Stupid): prefira a solução mais simples que resolve o problema. Complexidade só quando necessária.
- **DRY** (Don't Repeat Yourself): lógica duplicada vira função/serviço. Se copiou e colou, algo está errado.
- **YAGNI** (You Aren't Gonna Need It): não implemente o que não é necessário agora. Evite abstrações prematuras.
- **Clean Code**: nomes descritivos, funções pequenas com responsabilidade única, sem comentários óbvios, sem código morto.
- **SOLID**:
  - *Single Responsibility*: cada classe/serviço faz uma coisa só
  - *Open/Closed*: aberto para extensão, fechado para modificação
  - *Liskov Substitution*: subtipos substituem o tipo base sem quebrar comportamento
  - *Interface Segregation*: interfaces pequenas e específicas
  - *Dependency Inversion*: dependa de abstrações, não de implementações concretas

## Débito Técnico Conhecido

### Variáveis de Ambiente — Simplificação Pendente

Atualmente o projeto mantém dois formatos paralelos de variáveis AWS:
- `AWS__BucketName` (formato ASP.NET Core / Render)
- `AWS_BUCKET_NAME` (formato .env / código legado)

Isso viola DRY e aumenta superfície de erro. O mapeamento bidirecional em `Program.cs` é um workaround temporário.

**Plano de simplificação (próxima versão):**
1. Padronizar tudo no formato `AWS__*` (ASP.NET Core nativo via `IConfiguration`)
2. Remover todas as chamadas diretas a `Environment.GetEnvironmentVariable("AWS_*")`
3. Injetar `IConfiguration` nos endpoints que precisam de bucket/region
4. Remover o bloco `MapAspNetToEnv` do `Program.cs`
5. No Render: manter apenas `AWS__BucketName`, `AWS__Region`, `AWS__AccessKey`, `AWS__SecretKey`
6. No `.env` local: usar o mesmo formato `AWS__*`

Resultado: uma única fonte de verdade via `IConfiguration`, sem mapeamento manual.

> ⚠️ Lição aprendida: nome de bucket S3 é exato — um typo de uma letra aponta para um bucket inexistente.
> `aspec-captura` ≠ `aspec-capture` — o erro retornado é genérico (`Município não encontrado`)
> porque o S3 retorna 404 tanto para bucket errado quanto para arquivo inexistente.
> Sempre validar o `AWS__BucketName` contra o nome exato no console AWS antes do deploy.

## Dockerfile

- O `Dockerfile` referencia `AspecCapturaApi.csproj` e `AspecCapturaApi.dll` (nome atual)
- Nome antigo `pwa-camera-poc-api` foi descontinuado — nunca usar em novos arquivos
- Se o projeto for renomeado novamente, atualizar: `COPY`, `RUN dotnet restore`, `RUN dotnet build`, `RUN dotnet publish`, e `ENTRYPOINT`

## Testes — Autenticação

- Os testes de integração usam `CaptureTestFactory.CreateAuthenticatedClient()`
- O JWT de teste usa a constante `TestJwtSecret` definida na factory
- Endpoints protegidos com `[Authorize]` retornam `403` quando o prefixo do token não bate com o da requisição
- `JWT_SECRET` deve ser injetado via `Environment.SetEnvironmentVariable` na factory para testes
