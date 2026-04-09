# Auditoria de Segurança - .gitignore API

## 🚨 Status: ARQUIVOS SENSÍVEIS DETECTADOS NO GIT

Data: 9 de abril de 2026

## ❌ Arquivos Sensíveis Já Commitados

Os seguintes arquivos sensíveis foram encontrados no histórico do Git e devem ser removidos:

### Crítico (Contém Credenciais)
1. **`appsettings.Development.json`**
   - Contém: Credenciais AWS (AccessKey, SecretKey)
   - Risco: CRÍTICO
   - Ação: Remover do histórico do Git

### Alto (Podem Conter Dados Sensíveis)
2. **`build-output.txt`**
   - Contém: Logs de build (podem ter tokens/credenciais)
   - Risco: ALTO
   - Ação: Remover do histórico do Git

3. **`tail.txt`**
   - Contém: Logs de aplicação
   - Risco: MÉDIO
   - Ação: Remover do histórico do Git

4. **`test-log.txt`** e **`test-log-utf8.txt`**
   - Contém: Logs de testes (podem ter dados sensíveis)
   - Risco: MÉDIO
   - Ação: Remover do histórico do Git

### Médio (Arquivos Temporários)
5. **`commit-message-v0.8.0.txt`**
   - Contém: Mensagem de commit temporária
   - Risco: BAIXO
   - Ação: Remover do histórico do Git

6. **`test.txt`**
   - Contém: Arquivo de teste temporário
   - Risco: BAIXO
   - Ação: Remover do histórico do Git

## ✅ Correções Implementadas

### 1. Novo .gitignore Robusto
- ✅ Criado .gitignore completo com 500+ regras de segurança
- ✅ Alinhado com o .gitignore do Blazor PWA
- ✅ Inclui todas as categorias de arquivos sensíveis

### 2. Regras Específicas Adicionadas
```gitignore
# Credenciais
.env
.env.*
!.env.example
appsettings.*.json
!appsettings.json
!appsettings.Development.json.example

# Logs e arquivos temporários
build*.txt
tail.txt
test-log*.txt
test.txt
commit-message*.txt

# Arquivos HTTP (podem conter tokens)
*.http
!*.http.example

# Comandos rápidos (podem conter credenciais)
COMANDOS_RAPIDOS.md
```

## 🔧 Ações Necessárias

### Opção 1: Remover do Histórico (Recomendado para Produção)

Se este repositório for público ou compartilhado:

```bash
# ATENÇÃO: Isso reescreve o histórico do Git!
# Faça backup antes de executar

# Remover arquivos sensíveis do histórico
git filter-branch --force --index-filter \
  "git rm --cached --ignore-unmatch appsettings.Development.json build-output.txt tail.txt test-log.txt test-log-utf8.txt commit-message-v0.8.0.txt test.txt" \
  --prune-empty --tag-name-filter cat -- --all

# Forçar push (CUIDADO!)
git push origin --force --all
git push origin --force --tags
```

### Opção 2: Apenas Parar de Rastrear (MVP/Testes)

Se este é um repositório privado de testes:

```bash
# Parar de rastrear os arquivos (mantém histórico)
git rm --cached appsettings.Development.json
git rm --cached build-output.txt
git rm --cached tail.txt
git rm --cached test-log.txt
git rm --cached test-log-utf8.txt
git rm --cached commit-message-v0.8.0.txt
git rm --cached test.txt

# Commit
git add .gitignore
git commit -m "security: update .gitignore and stop tracking sensitive files"
```

### Opção 3: Usar BFG Repo-Cleaner (Mais Rápido)

```bash
# Instalar BFG
# https://rtyley.github.io/bfg-repo-cleaner/

# Remover arquivos
bfg --delete-files appsettings.Development.json
bfg --delete-files build-output.txt
bfg --delete-files tail.txt
bfg --delete-files test-log.txt
bfg --delete-files test-log-utf8.txt
bfg --delete-files commit-message-v0.8.0.txt
bfg --delete-files test.txt

# Limpar
git reflog expire --expire=now --all
git gc --prune=now --aggressive

# Push
git push origin --force --all
```

## 📋 Checklist de Segurança

- [x] Criar .gitignore robusto
- [x] Identificar arquivos sensíveis commitados
- [ ] Escolher método de remoção (Opção 1, 2 ou 3)
- [ ] Executar remoção dos arquivos sensíveis
- [ ] Verificar que arquivos não estão mais rastreados
- [ ] Revogar credenciais AWS expostas (se aplicável)
- [ ] Gerar novas credenciais AWS
- [ ] Atualizar .env com novas credenciais
- [ ] Verificar que .env não está commitado
- [ ] Commit do novo .gitignore
- [ ] Comunicar time sobre mudanças

## 🔐 Avaliação de Risco

### Contexto Atual (MVP/Testes)
- Repositório: Privado
- Dados: Fictícios
- Credenciais: S3 pessoal de testes
- Risco Atual: **BAIXO**

### Se Migrar para Produção
- Risco Potencial: **CRÍTICO**
- Ação Obrigatória: Remover do histórico + Revogar credenciais

## 📚 Referências

- [GitHub: Removing sensitive data](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)
- [BFG Repo-Cleaner](https://rtyley.github.io/bfg-repo-cleaner/)
- [Git filter-branch](https://git-scm.com/docs/git-filter-branch)

## 🎯 Próximos Passos

1. **Imediato**: Parar de rastrear arquivos sensíveis (Opção 2)
2. **Curto Prazo**: Validar que novos commits não incluem arquivos sensíveis
3. **Antes de Produção**: Limpar histórico do Git (Opção 1 ou 3)
4. **Antes de Produção**: Revogar e regenerar todas as credenciais

---

**Nota**: Este relatório foi gerado automaticamente pela auditoria de segurança do .gitignore.
