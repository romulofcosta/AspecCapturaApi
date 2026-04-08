# Correção: Erro de Conversão JSON na Sincronização de Lotes

## Problema Identificado

Após autenticação e ao recuperar os lotes de tombamento, ocorria erro de conversão JSON:
```
The JSON value could not be converted to...
```

## Causa Raiz

Incompatibilidade entre as estruturas de dados da API e do Frontend:

### API (`TombamentoRecord`)
A classe `TombamentoRecord` na API não possuía 3 campos que o frontend esperava receber.

### Frontend (`TombamentoWire`)
A classe `TombamentoWire` no frontend esperava deserializar os seguintes campos adicionais:
- `descricao` (string?)
- `localizacao` (string?)
- `valorestimado` (decimal?)

## Solução Aplicada

Adicionados os 3 campos faltantes ao `TombamentoRecord` em `pwa-camera-poc-api/Models/ApiModels.cs`:

```csharp
// Campos adicionais para compatibilidade com frontend
[JsonPropertyName("descricao")] public string? Descricao { get; set; }
[JsonPropertyName("localizacao")] public string? Localizacao { get; set; }
[JsonPropertyName("valorestimado")] public decimal? ValorEstimado { get; set; }
```

## Impacto

- Os campos são nullable, portanto não quebram dados existentes
- A serialização JSON agora é compatível entre API e Frontend
- O processo de sincronização de lotes deve funcionar corretamente

## Próximos Passos

1. Testar a sincronização localmente após autenticação
2. Verificar se os lotes são baixados sem erros
3. Confirmar que os dados são armazenados corretamente no IndexedDB

## Arquivos Modificados

- `pwa-camera-poc-api/Models/ApiModels.cs` - Adicionados 3 campos ao `TombamentoRecord`
