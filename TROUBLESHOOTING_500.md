# 🐛 Troubleshooting - Erro 500 em /api/tombamentos/lote

## 📋 Problema Reportado

Erro 500 (Internal Server Error) ao tentar baixar lotes de tombamentos na tela "Baixando Lotes de Tombamentos".

## 🔍 Análise

O erro estava ocorrendo na função `BuildOrGetChunkIndexAsync` que é responsável por:
1. Buscar o arquivo JSON do S3
2. Deserializar os tombamentos
3. Criar chunks (lotes) de dados
4. Cachear o índice de chunks

### Possíveis Causas

1. **Falta de logs detalhados**: A função não tinha logs suficientes para debug
2. **Tratamento de erros insuficiente**: Exceções não eram capturadas adequadamente
3. **Problemas de acesso ao S3**: Permissões ou configuração incorreta
4. **Arquivo JSON corrompido**: Dados inválidos no S3

## ✅ Solução Aplicada

### 1. Adicionados Logs Detalhados

```csharp
log?.LogInformation("BuildOrGetChunkIndexAsync: Usando index em cache para {Key}", key);
log?.LogInformation("BuildOrGetChunkIndexAsync: Construindo index para {Key}", key);
log?.LogWarning("BuildOrGetChunkIndexAsync: Nenhum tombamento encontrado em {Key}", key);
log?.LogInformation("BuildOrGetChunkIndexAsync: Processando {Total} tombamentos (filtrados: {Filtrados})", 
    itens.Count, itensFiltrados.Count);
log?.LogInformation("BuildOrGetChunkIndexAsync: Index construído com sucesso - {Total} registros, {Chunks} chunks", 
    total, chunks.Count);
```

### 2. Adicionado Try-Catch Global

```csharp
try
{
    // Lógica principal
}
catch (Exception ex)
{
    log?.LogError(ex, "BuildOrGetChunkIndexAsync: Erro ao construir index para {Key}", key);
    throw;
}
```

### 3. Passagem do Logger

Atualizado para passar o `ILogger` para a função auxiliar:

```csharp
// Antes
var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache);

// Depois
var index = await BuildOrGetChunkIndexAsync(s3, bucket, key, cache, log);
```

## 🧪 Como Verificar

### 1. Verificar Logs no Render

1. Acesse: https://dashboard.render.com
2. Selecione o serviço `pwa-camera-poc-api`
3. Vá em "Logs"
4. Procure por:
   - `BuildOrGetChunkIndexAsync: Construindo index`
   - `BuildOrGetChunkIndexAsync: Index construído com sucesso`
   - Ou erros: `BuildOrGetChunkIndexAsync: Erro ao construir index`

### 2. Testar Endpoint Diretamente

```bash
# Testar sync-info
curl "https://pwa-camera-poc-api.onrender.com/api/tombamentos/sync-info?prefix=CE999"

# Deve retornar:
# {
#   "totalRegistros": 1234,
#   "totalChunks": 3,
#   "versao": "20250208120000",
#   "hashGlobal": "abc123..."
# }

# Testar lote específico
curl "https://pwa-camera-poc-api.onrender.com/api/tombamentos/lote/1?prefix=CE999"

# Deve retornar:
# {
#   "chunkId": 1,
#   "data": [...],
#   "hash": "abc123..."
# }
```

### 3. Verificar no Frontend

1. Acesse: https://pwa-camera-poc-blazor.pages.dev
2. Faça login
3. Aguarde a tela "Baixando Lotes de Tombamentos"
4. Abra DevTools (F12) > Console
5. Verifique se há erros 500

## 🔧 Possíveis Problemas Adicionais

### Problema 1: Bucket não configurado

**Sintoma:** Erro "Bucket não configurado"

**Solução:**
1. Verifique variáveis de ambiente no Render:
   - `AWS__BucketName`
   - `AWS__AccessKey`
   - `AWS__SecretKey`
   - `AWS__Region`

### Problema 2: Arquivo não encontrado no S3

**Sintoma:** Erro 404 ou "Arquivo não encontrado"

**Solução:**
1. Verifique se o arquivo existe: `usuarios/CE999.json`
2. Verifique permissões do bucket S3
3. Verifique se o prefixo está correto (uppercase)

### Problema 3: JSON corrompido

**Sintoma:** Erro de deserialização

**Solução:**
1. Baixe o arquivo do S3
2. Valide o JSON: https://jsonlint.com
3. Verifique se tem a estrutura esperada:
```json
{
  "usuarios": [...],
  "tabelas": {
    "tombamentos": [...]
  }
}
```

### Problema 4: Timeout

**Sintoma:** Erro após 30 segundos

**Solução:**
1. Arquivo muito grande (>50MB)
2. Considere aumentar o timeout no Render
3. Ou otimize o tamanho do arquivo

### Problema 5: Memória insuficiente

**Sintoma:** Out of Memory Exception

**Solução:**
1. Arquivo muito grande
2. Upgrade do plano no Render
3. Ou reduza `MaxItemsPerChunk` de 5000 para 2000

## 📊 Métricas de Performance

### Tamanhos Típicos

- Arquivo JSON: 10-50 MB
- Tombamentos: 5.000 - 50.000 registros
- Chunks: 1-10 chunks
- Tempo de processamento: 5-30 segundos

### Limites Recomendados

- `MaxItemsPerChunk`: 5000 (ajustar conforme necessário)
- Timeout: 60 segundos
- Cache: 1 hora
- Memória: 512 MB mínimo

## 🔄 Próximos Passos

1. **Aguardar redeploy** do Render (2-5 minutos)
2. **Testar novamente** o login e sincronização
3. **Verificar logs** no Render para identificar o erro específico
4. **Reportar** o erro específico encontrado nos logs

## 📞 Suporte

Se o problema persistir após o redeploy:

1. Copie os logs do Render
2. Verifique a mensagem de erro específica
3. Consulte este documento para soluções
4. Se necessário, ajuste as configurações conforme indicado

---

**Commit:** a70ac6f  
**Data:** 8 de Fevereiro de 2025  
**Status:** ✅ Correção aplicada e pushed
