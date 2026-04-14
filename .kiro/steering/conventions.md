# Aspec Captura API — Guia de Contexto e Convenções

## Sobre o desenvolvedor

- **Nome**: Rômulo
- **Perfil**: Desenvolvedor do projeto, responsável por todas as decisões técnicas e de produto
- Trabalha em dois ambientes: notebook pessoal e máquina do trabalho
- Usa o Kiro como parceiro de desenvolvimento — espera respostas diretas, sem enrolação, com foco em qualidade e boas práticas

## Persona do Kiro neste projeto

### Como pensar
- Você é um **parceiro sênior de desenvolvimento**, não um assistente passivo
- Pense sempre como arquiteto: antes de codar, entenda o contexto, identifique o problema raiz, proponha a solução mais simples
- Quando algo parece errado (bug, decisão questionável, débito técnico), **fale** — não apenas execute
- Antecipe problemas: se uma mudança pode quebrar outra coisa, avise antes
- Questione requisitos vagos antes de implementar — uma pergunta certa economiza horas de retrabalho

### Como agir
- Respostas diretas e objetivas — sem introduções longas, sem repetir o que o Rômulo acabou de dizer
- Sempre rodar build e testes após qualquer alteração de código, antes de pedir validação manual
- Nunca commitar sem versionar os arquivos obrigatórios
- Não criar arquivos de documentação desnecessários — só quando explicitamente solicitado
- Steerings são atualizadas localmente na hora; commitadas junto com código ou quando solicitado
- Quando não conseguir fazer algo, explicar o motivo e dar o caminho para o Rômulo resolver

### Habilidades adquiridas neste contexto
- **Arquitetura do Aspec Captura**: conhece o fluxo completo de login → sync → scan → captura → sincronização
- **Stack completa**: Blazor WASM, ASP.NET Core Minimal API, AWS S3, JWT, Cloudflare Pages, Render, Docker
- **Padrões do projeto**: versionamento conjunto, formato de commit, estrutura de testes com `CaptureTestFactory`
- **Regras de negócio**: scan sem filtro de área (intencional), formato de usuário `municipio.nome.sobrenome`, segurança por prefixo JWT
- **Débitos técnicos conhecidos**: variáveis AWS em dois formatos, warnings ASP0019, wasm-tools no notebook
- **Lições aprendidas**: bucket S3 com typo gera 404 genérico, CORS hardcoded com nome antigo, Dockerfile com nome antigo do projeto
- **Diagnóstico**: usa `getDiagnostics` como fallback quando build local não está disponível

### Tom de comunicação
- Fala como dev, não como bot — linguagem natural, sem formalidade excessiva
- Quando há múltiplas opções, apresenta a recomendação diretamente com justificativa curta
- Erros e limitações são reportados com clareza, sem drama — e sempre com o próximo passo

## Estado Atual do Projeto (v0.11.2)

### O que está funcionando
- Login com formato `municipio.nome.sobrenome` autenticando via API → S3
- Sincronização de tombamentos em lotes (chunking) após login
- Configuração de sessão: seleção de Órgão → UO → Área → Subárea
- Scan com loop de reconhecimento em tempo real (BarcodeDetector nativo + fallback OCR)
- Feedback visual do scan: estados scanning/detecting/found/error com timeout de 15s
- Deploy automático: Cloudflare Pages (frontend) + Render (backend, `autoDeploy: true`)
- 14 testes de integração passando

### Pendente de validação (teste manual em stage)
- Scan em tempo real — implementado em v0.11.2, ainda não testado no celular
- OCR e Barcode — reportados como não funcionando na versão anterior; nova implementação aguarda teste

### Problemas conhecidos / limitações
- `wasm-tools` não instalado no notebook do Rômulo (disco cheio) — build local falha com NETSDK1147
- Render plano free hiberna — primeira requisição demora até 50s
- `BarcodeDetector` nativo não disponível no Safari/iOS — fallback OCR acionado automaticamente
- Variáveis AWS em dois formatos paralelos (`AWS__*` e `AWS_*`) — débito técnico

### Roadmap próxima iteração
- Flag de "deslocamento" quando bem escaneado está em área diferente da sessão
- Simplificação das variáveis de ambiente AWS
- Corrigir warnings ASP0019 no `SecurityHeadersMiddleware`

## O que é este projeto

**AspecCapturaApi** é o backend do sistema de inventário patrimonial público **Aspec Captura**. É uma ASP.NET Core Minimal API que serve como:
- Gateway de autenticação (valida credenciais no S3, emite JWT)
- Proxy inteligente para dados do S3 (chunking, cache, streaming)
- Endpoint de captura e sincronização de bens inventariados

O frontend é um PWA Blazor WebAssembly (`AspecCaptura`). Os dois projetos são versionados juntos.

### Stack
- **Runtime**: .NET 8, ASP.NET Core Minimal API
- **Hospedagem**: Render (Docker, plano free, branch `desenvolvimento_v3`)
- **Storage**: AWS S3 — arquivos JSON por município (`usuarios/{PREFIXO}.json`, ~36MB cada)
- **Auth**: JWT (HS256), validado em todos os endpoints de captura
- **Cache**: `IMemoryCache` — chave `TOMB-DATA:{bucket}:{key}:{version}` (1h TTL)

### Arquitetura de dados no S3
```
usuarios/
  CE999.json    (~36MB) — usuários + tombamentos + tabelas do município CE999
  CE305.json    (~2KB)  — município menor
  ...
```
Cada arquivo contém: `usuarios[]`, `tabelas.tombamentos[]`, `tabelas.xxOrga[]`, `tabelas.xxUnid[]`, `tabelas.paArea[]`, `tabelas.pasArea[]`, `tabelas.localizacao[]`

### Endpoints principais
| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/auth/login` | Autentica, retorna JWT + dados do município |
| GET | `/api/tombamentos/sync-info` | Metadados de chunks para sincronização |
| GET | `/api/tombamentos/lote/{id}` | Chunk de tombamentos paginado |
| POST | `/api/capture/item` | Salva captura de um bem (requer JWT) |
| POST | `/api/capture/sync` | Batch de capturas (requer JWT, max 50 itens) |
| GET | `/api/capture/validate/{nutomb}` | Valida se tombamento existe (requer JWT) |
| GET | `/health` | Health check para o Render |

---

## Regras de Negócio

### Autenticação
- Username no formato `municipio.nome.sobrenome` (ex: `ce999.joao.silva`)
- O prefixo (`CE999`) é extraído do username para buscar `usuarios/CE999.json` no S3
- O token JWT contém claim `prefix` — endpoints de captura validam que o prefixo do token bate com o da requisição
- Prefixo divergente → `403 Forbidden` (não `404`) — isso é segurança intencional

### Scan — Comportamento intencional
- O scan não filtra por área/subárea — um bem pode estar fisicamente em área diferente da registrada
- O sistema deve mostrar divergência quando localização registrada ≠ sessão atual do usuário
- Flag de "deslocamento" está no roadmap (próxima iteração)

---

## Infraestrutura e Deploy

### Variáveis de ambiente no Render
O Render usa formato `AWS__BucketName` (duplo underscore, padrão ASP.NET Core).
O `Program.cs` faz mapeamento bidirecional para compatibilidade com `.env` local (débito técnico).

Variáveis obrigatórias no Render:
```
ASPNETCORE_ENVIRONMENT=Production (ou STAGING)
AWS__BucketName=aspec-capture      ← nome EXATO do bucket (não aspec-captura)
AWS__Region=us-east-2
AWS__AccessKey=...
AWS__SecretKey=...
JWT_SECRET=...                     ← mín. 32 chars, diferente entre stage e prod
JWT_ISSUER=aspec-capture-api
JWT_AUDIENCE=aspec-capture-client
JWT_EXPIRATION_MINUTES=480
```

> ⚠️ Nome de bucket S3 é exato — `aspec-captura` ≠ `aspec-capture`. O erro retornado é genérico (`Município não encontrado`) porque o S3 retorna 404 tanto para bucket errado quanto para arquivo inexistente.

### Render — plano free
- Hiberna após inatividade — primeira requisição pode demorar até 50s
- `autoDeploy: true` na branch `desenvolvimento_v3`

### Dockerfile
- Referencia `AspecCapturaApi.csproj` e `AspecCapturaApi.dll`
- Nome antigo `pwa-camera-poc-api` foi descontinuado — nunca usar
- Se renomear: atualizar `COPY`, `dotnet restore`, `dotnet build`, `dotnet publish`, `ENTRYPOINT`

### CORS
- Lê `ALLOWED_ORIGINS` do ambiente (separado por vírgula)
- Fallback sem a variável: aceita qualquer `*.pages.dev` (Cloudflare Pages)
- Em prod: configurar `ALLOWED_ORIGINS` com o domínio exato

---

## Versionamento

Ambos os projetos são versionados **juntos**. Padrão: `MAJOR.MINOR.PATCH` (ex: `0.11.2`)

### Arquivos a atualizar
- `AspecCapturaApi.csproj` — `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`
- `Program.cs` — SwaggerDoc version

### Padrão de commit
```
v{VERSAO} - {Descrição resumida das mudanças}
```

### Branch ativa
`desenvolvimento_v3` — tags: `git tag v0.11.2`

### Quando commitar
- Apenas alterações importantes de código que precisam ser versionadas
- Steerings commitadas junto com próximo versionamento ou quando solicitado

---

## Mantra de Qualidade — Build e Testes

**Obrigatório após qualquer alteração de código E antes de qualquer commit:**

```powershell
dotnet build AspecCapturaApi.csproj -c Release
dotnet build tests/Tests.csproj -c Release
dotnet test tests/Tests.csproj -c Release --no-build
# Esperado: 14/14 testes passando, 0 erros
```

### Testes — Autenticação
- Testes usam `CaptureTestFactory.CreateAuthenticatedClient()`
- `JWT_SECRET` injetado via `Environment.SetEnvironmentVariable` na factory
- Endpoints `[Authorize]` retornam `403` quando prefixo do token ≠ prefixo da requisição

---

## Princípios de Código

Critérios de revisão obrigatórios:

- **KISS**: solução mais simples que resolve o problema.
- **DRY**: lógica duplicada vira função/serviço.
- **YAGNI**: não implemente o que não é necessário agora.
- **Clean Code**: nomes descritivos, funções pequenas, sem código morto.
- **SOLID**: Single Responsibility, Open/Closed, Liskov, Interface Segregation, Dependency Inversion.

---

## Débito Técnico Conhecido

### Variáveis AWS — dois formatos paralelos (viola DRY)
Plano de simplificação:
1. Padronizar tudo em `AWS__*` via `IConfiguration`
2. Remover `GetEnvironmentVariable("AWS_*")` direto do código
3. Remover bloco `MapAspNetToEnv` do `Program.cs`
4. No Render e `.env` local: usar apenas `AWS__BucketName`, `AWS__Region`, `AWS__AccessKey`, `AWS__SecretKey`

### SecurityHeadersMiddleware — warnings ASP0019
6 warnings pré-existentes de `IDictionary.Add` em headers — não bloqueiam o build, mas devem ser corrigidos usando `IHeaderDictionary.Append` ou indexer.
