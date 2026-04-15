# 🔐 Backlog de Segurança — Aspec Captura API

**Time:** Rômulo + Kiro  
**Criado em:** 14 de abril de 2026  
**Status:** Aguardando priorização

---

## 🎯 OBJETIVO

Implementar camadas de segurança robustas na API do Aspec Captura, seguindo as melhores práticas de AppSec e DevSecOps. Este backlog será trabalhado de forma focada quando priorizarmos a esteira de segurança.

---

## 📋 BACKLOG PRIORIZADO

### 🔴 CRÍTICO — Bloqueadores de Produção

#### #1 — Revogar e Rotacionar Credenciais AWS Expostas
**Risco:** CRÍTICO  
**Esforço:** 30 minutos  
**Responsável:** Rômulo (ação manual)

**Descrição:**
Credenciais AWS foram expostas no `.env` local durante sessão de desenvolvimento. Precisam ser revogadas e substituídas.

**Credenciais Expostas:**
```
AWS_ACCESS_KEY_ID=<SUA_ACCESS_KEY_ID>
AWS_SECRET_ACCESS_KEY=<SEU_SECRET_ACCESS_KEY>
```

**Tarefas:**
- [ ] Revogar access key no AWS IAM Console
- [ ] Gerar novas credenciais com least privilege (política IAM restrita)
- [ ] Atualizar `.env` local
- [ ] Atualizar Render environment variables (stage e prod)
- [ ] Validar que a API continua funcionando

**Referência:** `SECURITY_ACTIONS_REQUIRED.md` (seção 1-5)

**Critério de Aceite:**
- Credenciais antigas desativadas no AWS IAM
- Novas credenciais funcionando em todos os ambientes
- Política IAM com permissões mínimas (apenas s3:GetObject, s3:PutObject no bucket específico)

---

### 🟠 ALTO — Vulnerabilidades Exploráveis

#### #2 — Implementar Rate Limiting em Endpoints de Autenticação
**Risco:** ALTO  
**Esforço:** 2 horas  
**Responsável:** Kiro + Rômulo

**Descrição:**
Endpoint `/api/auth/login` está desprotegido contra ataques de brute force. Atacante pode tentar milhares de senhas por segundo.

**Tarefas:**
- [ ] Instalar pacote `AspNetCoreRateLimit`
- [ ] Configurar rate limiting global (60 req/min por IP)
- [ ] Configurar rate limiting estrito para `/api/auth/login` (5 tentativas / 15 minutos)
- [ ] Adicionar headers de rate limit nas respostas (`X-RateLimit-Limit`, `X-RateLimit-Remaining`)
- [ ] Testar com múltiplas tentativas de login
- [ ] Documentar configuração no README

**Implementação:**
```csharp
// Program.cs
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "POST:/api/auth/login",
            Period = "15m",
            Limit = 5
        },
        new RateLimitRule
        {
            Endpoint = "*",
            Period = "1m",
            Limit = 60
        }
    };
});

app.UseIpRateLimiting();
```

**Critério de Aceite:**
- Após 5 tentativas de login falhas, retorna `429 Too Many Requests`
- Headers de rate limit presentes nas respostas
- Testes automatizados validando o comportamento

---

#### #3 — Gerar JWT Secret Forte e Rotacionar
**Risco:** ALTO  
**Esforço:** 5 minutos  
**Responsável:** Rômulo

**Descrição:**
JWT secret atual é fraco e previsível. Precisa ser substituído por um secret criptograficamente seguro (512 bits).

**Tarefas:**
- [ ] Gerar secret forte: `openssl rand -base64 64`
- [ ] Atualizar `.env` local
- [ ] Atualizar Render environment variables (stage e prod)
- [ ] Validar que tokens antigos são invalidados
- [ ] Documentar processo de rotação

**Critério de Aceite:**
- JWT secret >= 64 caracteres (512 bits)
- Tokens gerados antes da rotação são rejeitados
- Usuários precisam fazer login novamente após rotação

---

#### #4 — Validar Implementação de Hash de Senhas
**Risco:** ALTO  
**Esforço:** 1-4 horas (depende do estado atual)  
**Responsável:** Kiro + Rômulo

**Descrição:**
Código do `AuthService.ValidatePassword` não foi auditado. Precisa garantir que senhas são hasheadas com BCrypt ou Argon2, não comparação direta.

**Tarefas:**
- [ ] Auditar código do `AuthService.ValidatePassword`
- [ ] Se necessário, implementar BCrypt (pacote `BCrypt.Net-Next`)
- [ ] Migrar senhas existentes para hash (se aplicável)
- [ ] Adicionar salt único por senha
- [ ] Adicionar testes unitários para validação de senha
- [ ] Documentar algoritmo de hash usado

**Implementação (se necessário):**
```csharp
// AuthService.cs
public bool ValidatePassword(string plainPassword, string hashedPassword)
{
    return BCrypt.Net.BCrypt.Verify(plainPassword, hashedPassword);
}

public string HashPassword(string plainPassword)
{
    return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);
}
```

**Critério de Aceite:**
- Senhas nunca armazenadas em plain text
- Hash usa BCrypt ou Argon2 com salt
- Testes unitários cobrindo casos de sucesso e falha

---

#### #5 — Adicionar Validação de Prefixo em Endpoints Faltantes
**Risco:** MÉDIO-ALTO  
**Esforço:** 1 hora  
**Responsável:** Kiro + Rômulo

**Descrição:**
Endpoints como `/api/tombamentos/localizacoes` e `/api/tombamentos/sync-info` recebem `prefix` via query string mas não validam se o JWT do usuário tem permissão para esse prefix. Usuário do município A pode acessar dados do município B.

**Endpoints Vulneráveis:**
- `GET /api/tombamentos/localizacoes`
- `GET /api/tombamentos/sync-info`
- `GET /api/tombamentos/lote/{id}`
- `GET /api/capture/validate/{nutomb}`

**Tarefas:**
- [ ] Adicionar atributo `[Authorize]` nos endpoints
- [ ] Adicionar validação de prefixo em cada endpoint
- [ ] Retornar `403 Forbidden` se prefixo não bate
- [ ] Logar tentativas de acesso não autorizado
- [ ] Adicionar testes de integração validando autorização

**Implementação:**
```csharp
app.MapGet("/api/tombamentos/localizacoes", 
    [Authorize] async (
    [FromQuery] string prefix,
    ClaimsPrincipal user,
    ...) =>
{
    var userPrefix = user.FindFirst("prefix")?.Value;
    if (userPrefix != prefix.ToUpper())
    {
        log.LogWarning("Unauthorized access: user {User} tried to access {Prefix}", 
            user.Identity?.Name, prefix);
        return Results.Forbid();
    }
    // ... resto do código
});
```

**Critério de Aceite:**
- Todos os endpoints que recebem `prefix` validam autorização
- Testes automatizados validando que usuário A não acessa dados de B
- Logs de tentativas de acesso não autorizado

---

### 🟡 MÉDIO — Melhorias de Segurança

#### #6 — Restringir CORS do S3
**Risco:** MÉDIO  
**Esforço:** 30 minutos  
**Responsável:** Kiro + Rômulo

**Descrição:**
CORS do S3 está configurado com `AllowedOrigins = ["*"]`, permitindo que qualquer site faça requests ao bucket.

**Tarefas:**
- [ ] Atualizar configuração de CORS no `Program.cs`
- [ ] Restringir a domínios específicos (Cloudflare Pages stage e prod)
- [ ] Testar que frontend continua funcionando
- [ ] Validar que outros domínios são bloqueados

**Implementação:**
```csharp
AllowedOrigins = [
    "https://8c591cc1.pwa-camera-poc-blazor.pages.dev",  // Stage
    "https://aspec-captura.pages.dev"  // Prod (quando existir)
]
```

**Critério de Aceite:**
- Apenas domínios autorizados podem fazer requests ao S3
- Frontend stage e prod funcionando normalmente
- Teste manual validando bloqueio de outros domínios

---

#### #7 — Sanitizar Logs para Prevenir Exposição de Dados Sensíveis
**Risco:** BAIXO-MÉDIO  
**Esforço:** 30 minutos  
**Responsável:** Kiro + Rômulo

**Descrição:**
Logs podem expor dados sensíveis se usuário errar e colocar senha no campo de usuário.

**Tarefas:**
- [ ] Criar função de sanitização de inputs antes de logar
- [ ] Aplicar sanitização em todos os logs de autenticação
- [ ] Adicionar filtro para nunca logar tokens JWT
- [ ] Revisar todos os logs existentes
- [ ] Documentar política de logging

**Implementação:**
```csharp
public static class LogSanitizer
{
    public static string SanitizeUsername(string username)
    {
        // Mascara parte do username para logs
        if (string.IsNullOrEmpty(username) || username.Length < 4)
            return "***";
        
        return username.Substring(0, 2) + "***" + username.Substring(username.Length - 2);
    }
}

log.LogInformation("Login attempt for user {User}", LogSanitizer.SanitizeUsername(usuario));
```

**Critério de Aceite:**
- Nenhum log contém senhas ou tokens
- Usernames são parcialmente mascarados
- Política de logging documentada

---

#### #8 — Implementar SAST/DAST no CI/CD
**Risco:** MÉDIO  
**Esforço:** 4 horas  
**Responsável:** Kiro + Rômulo

**Descrição:**
Nenhuma análise automática de segurança no pipeline. Vulnerabilidades podem ir para prod sem detecção.

**Tarefas:**
- [ ] Criar workflow GitHub Actions `.github/workflows/security-scan.yml`
- [ ] Adicionar TruffleHog para detectar secrets
- [ ] Adicionar Security Code Scan para análise estática .NET
- [ ] Adicionar scan de dependências vulneráveis (`dotnet list package --vulnerable`)
- [ ] Adicionar Trivy para scan de imagem Docker
- [ ] Configurar para quebrar build em vulnerabilidades críticas
- [ ] Documentar processo no README

**Implementação:**
Ver `devsecops-dotnet-expert` skill para workflow completo.

**Critério de Aceite:**
- Pipeline quebra se secrets forem detectados
- Pipeline quebra se dependências com CVE crítico forem encontradas
- Pipeline quebra se imagem Docker tiver vulnerabilidades críticas
- Relatórios de segurança disponíveis no GitHub

---

### 🟢 BAIXO — Hardening e Boas Práticas

#### #9 — Migrar Secrets para AWS Secrets Manager
**Risco:** BAIXO (melhoria)  
**Esforço:** 6 horas  
**Responsável:** Kiro + Rômulo

**Descrição:**
Secrets ainda estão em variáveis de ambiente. Migrar para AWS Secrets Manager permite rotação automática e auditoria.

**Tarefas:**
- [ ] Criar secrets no AWS Secrets Manager
- [ ] Implementar código para ler secrets do Secrets Manager
- [ ] Configurar rotação automática (90 dias)
- [ ] Atualizar Render para usar IAM role (se possível)
- [ ] Remover secrets de variáveis de ambiente
- [ ] Documentar processo

**Critério de Aceite:**
- Secrets lidos do AWS Secrets Manager em runtime
- Rotação automática configurada
- Auditoria de acesso via CloudTrail

---

#### #10 — Implementar Pre-commit Hook para Detectar Secrets
**Risco:** BAIXO (prevenção)  
**Esforço:** 1 hora  
**Responsável:** Kiro + Rômulo

**Descrição:**
Prevenir que secrets sejam commitados acidentalmente.

**Tarefas:**
- [ ] Criar script `.git/hooks/pre-commit`
- [ ] Integrar TruffleHog no hook
- [ ] Testar com commit contendo secret fake
- [ ] Documentar instalação do hook no README

**Implementação:**
```bash
#!/bin/bash
# .git/hooks/pre-commit
trufflehog git file://. --since-commit HEAD --only-verified --fail
if [ $? -ne 0 ]; then
  echo "❌ Secrets detected! Commit aborted."
  exit 1
fi
```

**Critério de Aceite:**
- Commit com secret é bloqueado
- Mensagem clara para o desenvolvedor
- Documentação de instalação

---

#### #11 — Implementar 2FA no Login
**Risco:** BAIXO (melhoria)  
**Esforço:** 8 horas  
**Responsável:** Kiro + Rômulo

**Descrição:**
Adicionar camada extra de segurança com autenticação de dois fatores (TOTP).

**Tarefas:**
- [ ] Implementar geração de QR code para TOTP
- [ ] Adicionar endpoint de configuração de 2FA
- [ ] Adicionar validação de código TOTP no login
- [ ] Adicionar códigos de recuperação
- [ ] Atualizar frontend para suportar 2FA
- [ ] Documentar processo para usuários

**Critério de Aceite:**
- Usuários podem habilitar 2FA
- Login requer código TOTP quando 2FA está ativo
- Códigos de recuperação funcionam

---

#### #12 — Configurar WAF (Web Application Firewall)
**Risco:** BAIXO (defesa em profundidade)  
**Esforço:** 4 horas  
**Responsável:** Rômulo

**Descrição:**
Adicionar camada de proteção contra ataques comuns (SQL injection, XSS, DDoS).

**Tarefas:**
- [ ] Avaliar Cloudflare WAF vs AWS WAF
- [ ] Configurar regras básicas (OWASP Core Rule Set)
- [ ] Configurar rate limiting no WAF
- [ ] Configurar geo-blocking (se necessário)
- [ ] Testar que aplicação continua funcionando
- [ ] Documentar configuração

**Critério de Aceite:**
- WAF ativo e bloqueando ataques comuns
- Logs de bloqueios disponíveis
- Aplicação funcionando normalmente

---

#### #13 — Contratar Penetration Testing
**Risco:** BAIXO (validação)  
**Esforço:** Externo  
**Responsável:** Rômulo

**Descrição:**
Contratar auditoria externa para validar todas as camadas de segurança.

**Tarefas:**
- [ ] Pesquisar empresas de pentest
- [ ] Solicitar orçamentos
- [ ] Agendar pentest
- [ ] Implementar correções dos achados
- [ ] Obter certificado de auditoria

**Critério de Aceite:**
- Pentest realizado por empresa certificada
- Relatório de vulnerabilidades recebido
- Todas as vulnerabilidades críticas corrigidas

---

## 📊 RESUMO EXECUTIVO

### Por Prioridade
- **Crítico:** 1 item (credenciais AWS)
- **Alto:** 4 itens (rate limiting, JWT secret, hash de senhas, validação de prefixo)
- **Médio:** 4 itens (CORS, logs, SAST/DAST, secrets manager)
- **Baixo:** 4 itens (pre-commit hook, 2FA, WAF, pentest)

### Por Esforço
- **< 1 hora:** 4 itens
- **1-4 horas:** 5 itens
- **4-8 horas:** 3 itens
- **> 8 horas:** 1 item

### Estimativa Total
- **Fase 1 (Crítico + Alto):** ~8 horas
- **Fase 2 (Médio):** ~11 horas
- **Fase 3 (Baixo):** ~13 horas
- **Total:** ~32 horas de trabalho focado

---

## 🎯 ROADMAP SUGERIDO

### Sprint 1 — Fundação de Segurança (1 semana)
- #1 Credenciais AWS
- #2 Rate limiting
- #3 JWT secret
- #4 Hash de senhas
- #5 Validação de prefixo

**Resultado:** API segura para produção

### Sprint 2 — Automação e Hardening (1 semana)
- #6 CORS S3
- #7 Sanitização de logs
- #8 SAST/DAST no CI/CD
- #10 Pre-commit hook

**Resultado:** Pipeline de segurança automatizado

### Sprint 3 — Melhorias Avançadas (2 semanas)
- #9 AWS Secrets Manager
- #11 2FA
- #12 WAF

**Resultado:** Defesa em profundidade completa

### Sprint 4 — Validação Externa (quando orçamento permitir)
- #13 Penetration testing

**Resultado:** Certificação de segurança

---

## 📝 NOTAS

- Este backlog será trabalhado de forma focada quando priorizarmos a esteira de segurança
- Cada item tem critérios de aceite claros
- Estimativas são aproximadas — ajustar conforme necessário
- Prioridades podem mudar baseado em novos riscos identificados

---

**Última atualização:** 14 de abril de 2026  
**Próxima revisão:** Quando iniciarmos a esteira de segurança  
**Time:** Rômulo + Kiro 🤝
