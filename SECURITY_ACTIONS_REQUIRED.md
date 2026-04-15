# 🚨 AÇÕES DE SEGURANÇA NECESSÁRIAS — CRÍTICO

**Data:** 14 de abril de 2026  
**Responsável:** Rômulo  
**Prioridade:** CRÍTICA — Executar IMEDIATAMENTE

---

## ⚠️ SITUAÇÃO ATUAL

Credenciais AWS foram expostas no arquivo `.env` que foi commitado no repositório Git. Isso representa um **risco crítico de segurança**.

### Credenciais Expostas

```
AWS_ACCESS_KEY_ID=<SUA_ACCESS_KEY_ID>
AWS_SECRET_ACCESS_KEY=<SEU_SECRET_ACCESS_KEY>
```

**Impacto:** Qualquer pessoa com acesso ao repositório (incluindo histórico do Git) tem acesso TOTAL ao bucket S3 `aspec-captura`, podendo:
- Ler dados de TODOS os municípios
- Modificar ou deletar dados
- Gerar custos AWS
- Comprometer a integridade do sistema

---

## 🔥 AÇÕES IMEDIATAS (AGORA)

### 1. Revogar Credenciais AWS Expostas

**Console AWS IAM:**
1. Acesse: https://console.aws.amazon.com/iam/
2. Navegue: Users → [seu usuário] → Security credentials
3. Localize a access key: `<SUA_ACCESS_KEY_ID>`
4. Clique em "Deactivate" ou "Delete"
5. Confirme a ação

**Ou via AWS CLI:**
```bash
aws iam delete-access-key \
  --access-key-id <SUA_ACCESS_KEY_ID> \
  --user-name [SEU_USUARIO_IAM]
```

### 2. Remover `.env` do Histórico do Git

**⚠️ IMPORTANTE:** Apenas deletar o arquivo não é suficiente — ele ainda está no histórico do Git.

**Opção A: BFG Repo-Cleaner (Recomendado — Mais Rápido)**

```bash
# 1. Instalar BFG
# Windows (via Chocolatey): choco install bfg-repo-cleaner
# macOS (via Homebrew): brew install bfg
# Ou baixar: https://rtyley.github.io/bfg-repo-cleaner/

# 2. Fazer backup do repo
cd ..
git clone --mirror https://github.com/romulofcosta/AspecCapturaApi.git aspec-backup.git

# 3. Limpar o arquivo .env do histórico
cd AspecCapturaApi
bfg --delete-files .env

# 4. Limpar referências antigas
git reflog expire --expire=now --all
git gc --prune=now --aggressive

# 5. Force push (⚠️ CUIDADO: isso reescreve o histórico)
git push --force
```

**Opção B: git filter-branch (Alternativa)**

```bash
git filter-branch --force --index-filter \
  "git rm --cached --ignore-unmatch AspecCapturaApi/.env" \
  --prune-empty --tag-name-filter cat -- --all

git reflog expire --expire=now --all
git gc --prune=now --aggressive
git push --force
```

**⚠️ ATENÇÃO:** Force push reescreve o histórico. Avise qualquer colaborador para fazer `git pull --rebase` após o push.

### 3. Criar Novas Credenciais AWS (Least Privilege)

**Console AWS IAM:**
1. Users → [seu usuário] → Security credentials → Create access key
2. Selecione "Application running outside AWS"
3. Anote as credenciais (você só verá o secret uma vez)

**Política IAM Recomendada (Least Privilege):**

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ReadOnlyS3Bucket",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:GetObjectMetadata",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::aspec-captura",
        "arn:aws:s3:::aspec-captura/*"
      ]
    },
    {
      "Sid": "WriteOnlyForCapture",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject"
      ],
      "Resource": [
        "arn:aws:s3:::aspec-captura/usuarios/*.json"
      ]
    },
    {
      "Sid": "CORSConfiguration",
      "Effect": "Allow",
      "Action": [
        "s3:PutBucketCORS",
        "s3:GetBucketCORS"
      ],
      "Resource": "arn:aws:s3:::aspec-captura"
    }
  ]
}
```

**Aplicar política:**
1. IAM → Policies → Create policy
2. Cole o JSON acima
3. Nome: `AspecCapturaApiLeastPrivilege`
4. Attach à sua user IAM

### 4. Atualizar `.env` Local

```bash
# 1. Copiar exemplo
cp .env.example .env

# 2. Editar .env com suas novas credenciais
# AWS_ACCESS_KEY_ID=NOVA_ACCESS_KEY
# AWS_SECRET_ACCESS_KEY=NOVO_SECRET_KEY

# 3. Gerar JWT_SECRET forte
openssl rand -base64 64

# 4. Preencher JWT_SECRET no .env
```

### 5. Atualizar Secrets no Render

**Render Dashboard:**
1. Acesse: https://dashboard.render.com/
2. Selecione seu serviço: `aspec-captura-api`
3. Environment → Environment Variables
4. Atualizar:
   - `AWS__AccessKey` → Nova access key
   - `AWS__SecretKey` → Novo secret key
   - `JWT_SECRET` → Novo secret forte (openssl rand -base64 64)
5. Salvar (Render fará redeploy automático)

---

## 📋 AÇÕES COMPLEMENTARES (HOJE)

### 6. Implementar Rate Limiting

**Já implementado no código.** Instalar pacote:

```bash
cd AspecCapturaApi
dotnet add package AspNetCoreRateLimit
```

### 7. Gerar JWT_SECRET Forte

```bash
# Gerar secret de 512 bits (64 bytes)
openssl rand -base64 64

# Exemplo de output:
# xK8fJ2mP9vL3nQ6rT4wY7zB1cE5gH8jM0oS2uV4xA6dF9hK1lN3pR5tW7yZ0bC3e
```

Use este valor em:
- `.env` local
- Render environment variables (`JWT_SECRET`)

### 8. Verificar `.gitignore`

Confirmar que `.env` está no `.gitignore`:

```bash
grep "^\.env$" .gitignore
# Deve retornar: .env
```

Se não estiver, adicionar:

```bash
echo ".env" >> .gitignore
git add .gitignore
git commit -m "security: ensure .env is in .gitignore"
```

---

## 🔐 AÇÕES DE MÉDIO PRAZO (ESTA SEMANA)

### 9. Migrar para AWS Secrets Manager

**Por que?**
- Secrets não ficam em variáveis de ambiente
- Rotação automática de credenciais
- Auditoria de acesso
- Criptografia gerenciada pela AWS

**Como implementar:**

```csharp
// Program.cs
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

var secretName = "aspec-captura/prod";
var region = "us-east-2";

var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region));
var request = new GetSecretValueRequest { SecretId = secretName };
var response = await client.GetSecretValueAsync(request);

var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(response.SecretString);
builder.Configuration.AddInMemoryCollection(secrets!);
```

### 10. Implementar SAST no CI/CD

**GitHub Actions** (`.github/workflows/security-scan.yml`):

```yaml
name: Security Scan

on: [push, pull_request]

jobs:
  secrets-scan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      
      - name: TruffleHog Secrets Scan
        uses: trufflesecurity/trufflehog@main
        with:
          path: ./
          base: ${{ github.event.repository.default_branch }}
          head: HEAD
          extra_args: --only-verified

  dependency-scan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      
      - name: Check for vulnerable packages
        run: |
          dotnet list package --vulnerable --include-transitive
          if grep -E "(Critical|High)" <<< "$(dotnet list package --vulnerable)"; then
            echo "Critical or High vulnerabilities found!"
            exit 1
          fi
```

### 11. Configurar Alertas de Segurança

**AWS CloudWatch Alarms:**
- Alertar em tentativas de acesso negado ao S3
- Alertar em uso anormal de API (spike de requests)

**Render Logs:**
- Configurar alertas para logs de erro 401/403

---

## ✅ CHECKLIST DE VALIDAÇÃO

Após executar as ações acima, validar:

- [ ] Credenciais antigas revogadas no AWS IAM
- [ ] `.env` removido do histórico do Git (verificar com `git log --all --full-history -- .env`)
- [ ] Novas credenciais AWS criadas com least privilege
- [ ] `.env` local atualizado com novas credenciais
- [ ] Render environment variables atualizados
- [ ] JWT_SECRET forte gerado (>= 64 chars)
- [ ] `.env` no `.gitignore`
- [ ] `.env.example` commitado (sem secrets)
- [ ] Rate limiting instalado (`AspNetCoreRateLimit`)
- [ ] Build e testes passando
- [ ] Deploy em stage funcionando

---

## 📞 SUPORTE

Se precisar de ajuda em qualquer etapa:
1. Consulte a documentação AWS IAM: https://docs.aws.amazon.com/IAM/
2. Consulte a documentação do BFG: https://rtyley.github.io/bfg-repo-cleaner/
3. Peça ajuda ao Kiro (eu) para qualquer dúvida técnica

---

## 🎯 PRÓXIMOS PASSOS (APÓS CORREÇÕES)

1. Implementar 2FA no login
2. Adicionar CAPTCHA em endpoints públicos
3. Configurar WAF (Cloudflare/AWS WAF)
4. Implementar token refresh
5. Audit logging completo
6. Penetration testing

---

**⚠️ LEMBRE-SE:** Segurança não é um projeto — é um processo contínuo. Rotacione credenciais regularmente (90 dias) e mantenha dependências atualizadas.

---

**Última atualização:** 14 de abril de 2026  
**Autor:** Kiro (AI Assistant)
